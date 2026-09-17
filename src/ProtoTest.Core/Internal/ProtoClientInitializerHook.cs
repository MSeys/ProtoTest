namespace ProtoTest.Core;

/// <summary>
/// Global lifecycle hook responsible for selecting and executing a registered
/// <see cref="IProtoClientInitializer"/> for each named client prior to test execution.
/// </summary>
internal sealed class ProtoClientInitializerHook(IEnumerable<IProtoClientInitializer> initializers) : IProtoTestHook
{
    /// <summary>
    /// Set to <see cref="int.MinValue"/> to ensure clients are initialized before all other hooks.
    /// </summary>
    public int Order => int.MinValue;

    public async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var groups = initializers.GroupBy(
            initializer => new ClientKey(initializer.ClientType, initializer.Name),
            ClientKeyComparer.Instance);

        foreach (var group in groups)
        {
            using var clientOperation = context.Trace
                .Operation("client.initialize", $"Initialize · {group.Key.Name} ({group.Key.ClientType.Name})", "ProtoTest.Core")
                .During(ProtoTracePhase.Setup)
                .With("client.name", group.Key.Name)
                .With("client.type", group.Key.ClientType.FullName)
                .With("initializer.count", group.Count().ToString())
                .Begin();
            var initialized = false;

            // IEnumerable<T> service resolution preserves registration order, which lets
            // Configure determine provider precedence without integration-specific coupling.
            foreach (var initializer in group)
            {
                using var attempt = context.Trace
                    .Operation("client.initializer.attempt", $"Try · {initializer.GetType().Name}", "ProtoTest.Core")
                    .During(ProtoTracePhase.Setup)
                    .With("initializer.type", initializer.GetType().FullName)
                    .Begin();
                try
                {
                    if (await initializer.TryInitializeAsync(context))
                    {
                        attempt.SetAttribute("selected", "true");
                        attempt.Succeed();
                        initialized = true;
                        break;
                    }
                    attempt.SetAttribute("selected", "false");
                    attempt.Succeed();
                }
                catch (Exception exception)
                {
                    attempt.Fail(exception);
                    clientOperation.Fail(exception);
                    throw;
                }
            }

            if (!initialized)
            {
                var exception = new InvalidOperationException(
                    $"No registered initializer could create a client of type " +
                    $"'{group.Key.ClientType.Name}' with name '{group.Key.Name}'.");
                clientOperation.Fail(exception);
                throw exception;
            }

            clientOperation.Succeed();
        }
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
        => Task.CompletedTask;

    private readonly record struct ClientKey(Type ClientType, string Name);

    private sealed class ClientKeyComparer : IEqualityComparer<ClientKey>
    {
        public static ClientKeyComparer Instance { get; } = new();

        public bool Equals(ClientKey x, ClientKey y)
            => x.ClientType == y.ClientType
               && StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode(ClientKey obj)
            => HashCode.Combine(obj.ClientType, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
