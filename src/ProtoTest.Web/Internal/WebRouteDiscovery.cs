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
    private int _started;

    public async ValueTask DiscoverAsync(IWebBackend backend, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _started) != 0) return;
        if (!enabled) return;
        if (backend is not IWebBackendJavaScript javascript) return;
        try
        {
            var json = await javascript.EvaluateJsonAsync(VueRouteDiscovery.Script, cancellationToken);
            if (json is null) return;
            Interlocked.Exchange(ref _started, 1);
            WebPageInventory.Record(
                context,
                "Web",
                VueRouteDiscovery.Parse(json),
                "vue-router",
                new Dictionary<string, object> { ["web.session"] = sessionName });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Discovery stays unlatched, so a later navigation can try again; the failure is traced.
            context.Trace.WriteEvent(
                "web.page.discovery.failed",
                "Vue route discovery failed",
                traceSource,
                outcome: ProtoTraceOutcome.Unknown,
                exception: exception);
        }
    }
}
