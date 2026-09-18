namespace ProtoTest.Core.Internal;

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
            var clientId = $"client:{group.Key.ClientType.FullName}:{group.Key.Name}";
            var clientName = $"Client {group.Key.ClientType.Name} '{group.Key.Name}'";
            using var clientOperation = context.Trace
                .Operation("client.initialize", $"Initialize · {group.Key.Name} ({group.Key.ClientType.Name})", "ProtoTest.Core")
                .During(ProtoTracePhase.Setup)
                .For(ProtoTraceEntityKinds.Client, clientId)
                .With("client.name", group.Key.Name)
                .With("client.type", group.Key.ClientType.FullName)
                .Begin();
            var initialized = false;

            // IEnumerable<T> service resolution preserves registration order, which lets
            // Configure determine provider precedence without integration-specific coupling.
            foreach (var initializer in group)
            {
                try
                {
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
                        initialized = true;
                        break;
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
                    $"'{group.Key.ClientType.Name}' with name '{group.Key.Name}'.");
                clientOperation.Fail(exception);
                throw exception;
            }

            clientOperation.Succeed();
        }
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
        => Task.CompletedTask;
}
