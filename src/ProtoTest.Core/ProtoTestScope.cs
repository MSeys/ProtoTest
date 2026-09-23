namespace ProtoTest.Core;

/// <summary>
/// One test's lifecycle as a scope: start it from a prepared test, run the framework's body, then let
/// the scope complete the lifecycle. Teardown failures are already recorded as findings by the
/// lifecycle; the scope never lets them replace the result the test reported, so every adapter shares
/// one policy instead of four.
/// </summary>
public sealed class ProtoTestScope : IAsyncDisposable
{
    private readonly ProtoHost _host;
    private ProtoTestResult _result = ProtoTestResult.Unknown;
    private int _completed;

    private ProtoTestScope(ProtoHost host, ProtoExecutionContext context)
    {
        _host = host;
        Context = context;
    }

    /// <summary>The started test's execution context.</summary>
    public ProtoExecutionContext Context { get; }

    /// <summary>The result the framework reported; defaults to <see cref="ProtoTestResult.Unknown"/>.</summary>
    public ProtoTestResult Result
    {
        get => _result;
        set => _result = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Starts the test lifecycle for a prepared test. The lifecycle call is made synchronously, so the
    /// ambient context it sets is visible to the caller's flow and the test body that awaits this task
    /// still sees it.
    /// </summary>
    public static Task<ProtoTestScope> StartAsync(
        ProtoTestPreparation preparation,
        ProtoHost host,
        IProtoTestAttachmentPublisher? attachmentPublisher = null)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(host);
        return CompleteStartAsync(host, preparation.StartAsync(host, attachmentPublisher));
    }

    private static async Task<ProtoTestScope> CompleteStartAsync(
        ProtoHost host,
        Task<ProtoExecutionContext> started)
    {
        var context = await started.ConfigureAwait(false);
        return new ProtoTestScope(host, context);
    }

    /// <summary>
    /// Completes the lifecycle once. A teardown failure is recorded by the lifecycle as a finding and
    /// on the failing teardown operation; it is swallowed here so cleanup can never replace the
    /// test's own outcome.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
        {
            return;
        }

        try
        {
            await _host.CompleteTestAsync(Result);
        }
        catch (Exception)
        {
            // Recorded as a finding by ProtoTestLifecycle.TeardownAsync; nothing left to do here.
        }
    }
}
