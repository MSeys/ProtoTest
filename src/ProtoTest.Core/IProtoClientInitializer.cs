namespace ProtoTest.Core;

/// <summary>
/// Defines a contract for initializing and registering client instances (e.g., HttpClient, GrpcChannel)
/// into the active <see cref="ProtoExecutionContext"/>.
/// </summary>
public interface IProtoClientInitializer
{
    /// <summary>
    /// Gets the unique registration name for this client instance (e.g., "Default", "OrderService").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Asynchronously builds and registers the client into the specified test execution context.
    /// </summary>
    Task InitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default);
}