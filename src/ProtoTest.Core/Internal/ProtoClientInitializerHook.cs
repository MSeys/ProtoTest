namespace ProtoTest.Core.Internal;

/// <summary>
/// Runs the registered <see cref="IProtoClientInitializer"/>s before each test, so every named client
/// exists in the test's context before the body runs.
/// </summary>
internal sealed class ProtoClientInitializerHook(IEnumerable<IProtoClientInitializer> initializers) : IProtoTestHook
{
    /// <summary>
    /// Set to <see cref="int.MinValue"/> so clients initialize before every other hook.
    /// </summary>
    public int Order => ProtoHookOrder.First;

    public async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        // Protocols own their provider chains, so two protocols can share a client name without
        // shadowing each other. An unscoped provider (the ASP.NET Core transport, a stub) joins every
        // chain whose name it serves as the last provider, so it stays a lazy fallback per protocol.
        var scoped = initializers.Where(initializer => !string.IsNullOrWhiteSpace(initializer.Protocol)).ToArray();
        var unscoped = initializers.Where(initializer => string.IsNullOrWhiteSpace(initializer.Protocol)).ToArray();
        var chains = new List<ClientChain>();
        var covered = new HashSet<ClientKey>(ClientKeyComparer.Instance);

        foreach (var group in scoped.GroupBy(
                     initializer => new ClientKey(
                         initializer.ClientType,
                         ProtoClientResolution.ScopedName(initializer.Protocol, initializer.Name)),
                     ClientKeyComparer.Instance))
        {
            var name = group.First().Name;
            covered.Add(new ClientKey(group.Key.ClientType, name));
            var providers = group
                .Cast<IProtoClientInitializer>()
                .Concat(unscoped.Where(initializer =>
                    initializer.ClientType == group.Key.ClientType
                    && string.Equals(initializer.Name, name, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            chains.Add(new ClientChain(
                group.First().Protocol,
                group.Key.ClientType,
                name,
                ProtoClientResolution.ScopedName(group.First().Protocol, name),
                providers));
        }

        foreach (var group in unscoped
                     .Where(initializer => !covered.Contains(new ClientKey(initializer.ClientType, initializer.Name)))
                     .GroupBy(
                         initializer => new ClientKey(initializer.ClientType, initializer.Name),
                         ClientKeyComparer.Instance))
        {
            chains.Add(new ClientChain(null, group.Key.ClientType, group.Key.Name, group.Key.Name, [.. group]));
        }

        var resolved = new List<InitializedClient>();

        // An unscoped provider serves every chain that names it, so it runs once per test and the
        // chains after the first reuse the client it registered. The memo is keyed by the provider
        // *and the chain's own name*, so a reused entry can only ever be the registration made for
        // that name - a differently named chain can never be served another chain's client.
        var fallbacks = new Dictionary<(IProtoClientInitializer Initializer, string Name), InitializedClient?>(
            FallbackKeyComparer.Instance);
        foreach (var chain in chains)
        {
            await InitializeChainAsync(context, chain, resolved, fallbacks);
        }

        // A client owned by exactly one provider group is also reachable by its bare name, so
        // context.Client<T>("Name") keeps working. An ambiguous name is deliberately not aliased; the
        // registry's miss message then lists the scoped candidates.
        foreach (var byName in resolved.GroupBy(item => (item.ClientType, item.Name)))
        {
            var clients = byName
                .Select(item => context.TryClient(byName.Key.ClientType, item.RegisteredName))
                .Where(client => client is not null)
                .Distinct(ReferenceEqualityComparer.Instance)
                .ToArray();
            if (clients.Length == 1)
            {
                context.RegisterClientAlias(byName.Key.ClientType, byName.Key.Name, clients[0]!);
            }
        }
    }

    private static async Task InitializeChainAsync(
        ProtoExecutionContext context,
        ClientChain chain,
        List<InitializedClient> resolved,
        Dictionary<(IProtoClientInitializer Initializer, string Name), InitializedClient?> fallbacks)
    {
        var clientId = $"client:{chain.ClientType.FullName}:{chain.ScopedName}";
        var clientName = $"Client {chain.ClientType.Name} '{chain.ScopedName}'";
        using var clientOperation = context.Trace
            .Operation("client.initialize", $"Initialize · {chain.ScopedName} ({chain.ClientType.Name})", ProtoCoreDiagnostics.TraceSource)
            .During(ProtoTracePhase.Setup)
            .For(ProtoTraceEntityKinds.Client, clientId)
            .With("client.name", chain.ScopedName)
            .With("client.protocol", chain.Protocol)
            .With("client.type", chain.ClientType.FullName)
            .Begin();
        var initialized = false;

        // IEnumerable<T> service resolution preserves registration order, which lets Configure determine
        // provider precedence without integration-specific coupling.
        foreach (var initializer in chain.Providers)
        {
            try
            {
                // An unscoped provider has already served its one invocation for this test when a
                // previous chain reached it for the same name: reuse what it registered, or its
                // decline, as it stands.
                if (initializer.Protocol is null && fallbacks.TryGetValue((initializer, chain.Name), out var cached))
                {
                    if (cached.HasValue)
                    {
                        resolved.Add(cached.Value);
                        initialized = true;
                        break;
                    }

                    continue;
                }

                if (await initializer.TryInitializeAsync(context))
                {
                    context.Trace.SetEntityState(
                        ProtoTraceEntityKinds.Client,
                        clientId,
                        clientName,
                        new Dictionary<string, string?>
                        {
                            ["client.initializer"] = initializer.GetType().Name
                        });
                    var entry = new InitializedClient(
                        initializer.Name,
                        initializer.ClientType,
                        ProtoClientResolution.ScopedName(initializer.Protocol, initializer.Name));
                    if (initializer.Protocol is null)
                    {
                        fallbacks[(initializer, chain.Name)] = entry;
                    }

                    resolved.Add(entry);
                    initialized = true;
                    break;
                }

                if (initializer.Protocol is null)
                {
                    fallbacks[(initializer, chain.Name)] = null;
                }
            }
            catch (Exception exception)
            {
                clientOperation.Fail(exception);
                throw;
            }
        }

        if (!initialized)
        {
            var exception = new InvalidOperationException(
                $"No registered initializer could create a client of type " +
                $"'{chain.ClientType.Name}' with name '{chain.ScopedName}'.");
            clientOperation.Fail(exception);
            throw exception;
        }

        clientOperation.Succeed();
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
        => Task.CompletedTask;

    private sealed record ClientChain(
        string? Protocol,
        Type ClientType,
        string Name,
        string ScopedName,
        IReadOnlyList<IProtoClientInitializer> Providers);

    // The memo identity: the provider instance plus the chain name it served, compared the same way
    // the chain join compares names (case-insensitively), because client names are case-insensitive.
    private sealed class FallbackKeyComparer : IEqualityComparer<(IProtoClientInitializer Initializer, string Name)>
    {
        public static FallbackKeyComparer Instance { get; } = new();

        public bool Equals(
            (IProtoClientInitializer Initializer, string Name) x,
            (IProtoClientInitializer Initializer, string Name) y)
            => ReferenceEquals(x.Initializer, y.Initializer)
               && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode((IProtoClientInitializer Initializer, string Name) obj)
            => HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Initializer),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }

    private readonly record struct InitializedClient(string Name, Type ClientType, string RegisteredName);
}

