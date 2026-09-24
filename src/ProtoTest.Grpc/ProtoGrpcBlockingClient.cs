namespace ProtoTest.Grpc;

using global::Grpc.Core;

/// <summary>
/// The synchronous facade of a <see cref="ProtoGrpcClient"/>: callers whose test code cannot await use
/// it to open streaming calls. Each member blocks the calling thread while authenticators and the
/// channel are prepared, running the async work on the pool with no captured synchronization context,
/// so an authenticator that awaits cannot deadlock against the blocked caller. Prefer
/// <see cref="ProtoGrpcClient.OpenServerStreamingAsync"/> and
/// <see cref="ProtoGrpcClient.OpenDuplexStreamingAsync"/> where the test can await.
/// </summary>
public sealed class ProtoGrpcBlockingClient(ProtoGrpcClient client)
{
    private readonly ProtoGrpcClient _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>Opens a server-streaming call, blocking until it is ready.</summary>
    public AsyncServerStreamingCall<TResponse> ServerStreaming<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        TRequest request,
        Action<Metadata>? metadata = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
        => RunOnPool(() => _client.OpenServerStreamingAsync(method, request, metadata, deadline, cancellationToken));

    /// <summary>Opens a duplex-streaming call, blocking until it is ready.</summary>
    public AsyncDuplexStreamingCall<TRequest, TResponse> DuplexStreaming<TRequest, TResponse>(
        Method<TRequest, TResponse> method,
        Action<Metadata>? metadata = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
        => RunOnPool(() => _client.OpenDuplexStreamingAsync(method, metadata, deadline, cancellationToken));

    private static T RunOnPool<T>(Func<Task<T>> open)
        => Task.Run(async () =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                return await open().ConfigureAwait(false);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }).GetAwaiter().GetResult();
}
