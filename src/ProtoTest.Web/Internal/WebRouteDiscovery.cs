namespace ProtoTest.Web.Internal;

using ProtoTest.Core;
using ProtoTest.Core.Internal;

/// <summary>
/// Opt-in Vue Router discovery (<c>DiscoverRoutes</c> on <c>[WebSession]</c> or
/// <c>Proto.Context.Web(...)</c>). It runs once per session after a navigation, answers with nothing
/// when Vue or its router is absent, and never fails the test; a genuine backend failure is recorded on
/// the trace instead.
/// </summary>
internal sealed class WebRouteDiscovery(
    ProtoExecutionContext context,
    string sessionName,
    string traceSource,
    bool enabled)
{
    private readonly ProtoLock _gate = new();
    private Task? _discovery;

    /// <summary>
    /// Runs the discovery once per session. Concurrent navigations await the same evaluation instead of
    /// each running the script, so the page inventory cannot record the same routes twice. A discovery
    /// that found no router, was cancelled, or failed leaves the memo unset, so a later navigation can
    /// try again.
    /// </summary>
    public Task DiscoverAsync(IWebBackend backend, CancellationToken cancellationToken)
    {
        if (!enabled || backend is not IWebBackendJavaScript javascript)
        {
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            return _discovery ??= DiscoverCoreAsync(javascript, cancellationToken);
        }
    }

    private async Task DiscoverCoreAsync(IWebBackendJavaScript javascript, CancellationToken cancellationToken)
    {
        // Yield first: the caller assigns the memo only after this method returns its task, so a
        // discovery that completes synchronously must not clear a memo that is not set yet.
        await Task.Yield();
        try
        {
            var json = await javascript.EvaluateJsonAsync(VueRouteDiscovery.Script, cancellationToken)
                .ConfigureAwait(false);
            if (json is null)
            {
                Retry();
                return;
            }

            WebPageInventory.Record(
                context,
                "Web",
                VueRouteParser.Parse(json),
                "vue-router",
                new Dictionary<string, object> { ["web.session"] = sessionName });
        }
        catch (OperationCanceledException)
        {
            // Discovery stays unlatched, so a later navigation can try again.
            Retry();
            throw;
        }
        catch (Exception exception)
        {
            // Discovery stays unlatched, so a later navigation can try again; the failure is traced.
            Retry();
            context.Trace.WriteEvent(
                "web.page.discovery.failed",
                "Vue route discovery failed",
                traceSource,
                outcome: ProtoTraceOutcome.Unknown,
                exception: exception);
        }
    }

    private void Retry()
    {
        lock (_gate)
        {
            _discovery = null;
        }
    }
}
