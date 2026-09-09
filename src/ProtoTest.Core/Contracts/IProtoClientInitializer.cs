namespace ProtoTest.Core;

/// <summary>
/// Defines a contract for initializing and registering client instances (e.g., HttpClient, GrpcChannel)
/// into the active <see cref="ProtoExecutionContext"/>.
/// </summary>
public interface IProtoClientInitializer
{
    /// <summary>
    /// Gets the registration name for this client (e.g., "Default", "OrderService").
    /// Initializers with the same name and <see cref="ClientType"/> form an ordered provider chain.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the type under which the client is registered in the execution context.
    /// </summary>
    Type ClientType { get; }

    /// <summary>
    /// Attempts to build and register the client into the specified test execution context.
    /// Returns <see langword="false"/> without changing the context when this initializer
    /// cannot provide the client for the current configuration.
    /// </summary>
    Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Defines a client initializer for a specific client type.
/// </summary>
/// <typeparam name="TClient">The type under which the client is registered in the execution context.</typeparam>
public interface IProtoClientInitializer<TClient> : IProtoClientInitializer where TClient : class
{
    Type IProtoClientInitializer.ClientType => typeof(TClient);
}
