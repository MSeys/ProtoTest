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
            var initialized = false;

            // IEnumerable<T> service resolution preserves registration order, which lets
            // Configure determine provider precedence without integration-specific coupling.
            foreach (var initializer in group)
            {
                if (await initializer.TryInitializeAsync(context))
                {
                    initialized = true;
                    break;
                }
            }

            if (!initialized)
            {
                throw new InvalidOperationException(
                    $"No registered initializer could create a client of type " +
                    $"'{group.Key.ClientType.Name}' with name '{group.Key.Name}'.");
            }
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
