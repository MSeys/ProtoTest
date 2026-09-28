namespace ProtoTest.Core;

/// <summary>
/// Defines a contract for initializing and registering client instances (e.g., HttpClient, GrpcChannel)
/// into the active <see cref="ProtoExecutionContext"/>.
/// </summary>
public interface IProtoClientInitializer
{
    /// <summary>
    /// Gets the registration name for this client (e.g., "Default", "OrderService").
    /// Initializers with the same <see cref="Protocol"/>, client type and name form an ordered provider
    /// chain.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the protocol this initializer serves (for example "Rest"), or <see langword="null"/> for a
    /// host-level client. Protocols keep their own provider chains, and a provider registers its client
    /// under <c>Protocol:Name</c> (see <see cref="ProtoClientResolution.ScopedName( string?, string)"/>),
    /// so two protocols can own the same client name without shadowing each other.
    /// </summary>
    string? Protocol => null;

    /// <summary>
    /// Gets the type under which the client is registered in the execution context.
    /// </summary>
    Type ClientType { get; }

    /// <summary>
    /// Attempts to build and register the client into the specified test execution context.
    /// Returns <see langword="false"/> without changing the context when this initializer
    /// cannot provide the client for the current configuration.
    /// <para>
    /// The test lifecycle carries the caller's cancellation token, or the runner's own where its
    /// adapter has one; an initializer reads it from
    /// <see cref="ProtoExecutionContext.CancellationToken"/> and passes it to its own I/O. An adapter
    /// whose extension point exposes no token (xUnit v3, TUnit) starts with
    /// <see cref="CancellationToken.None"/>; a run-scoped hook is the cancellable extension point.
    /// </para>
    /// </summary>
    Task<bool> TryInitializeAsync(ProtoExecutionContext context);
}

/// <summary>
/// Defines a client initializer for a specific client type.
/// </summary>
/// <typeparam name="TClient">The type under which the client is registered in the execution context.</typeparam>
public interface IProtoClientInitializer<TClient> : IProtoClientInitializer where TClient : class
{
    Type IProtoClientInitializer.ClientType => typeof(TClient);
}
