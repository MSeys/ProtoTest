namespace ProtoTest.Core.Internal;

internal sealed class ProtoSinkExportHook(
    IEnumerable<IProtoCollector> collectors,
    IEnumerable<IProtoReportSource> reportSources,
    IEnumerable<IProtoSink> sinks,
    ProtoTraceSession traceSession) : IProtoRunHook
{
    // AfterRun executes in descending order. Reports are generated after the gates and before the
    // run-scoped resources are released and the trace archive is written.
    public int Order => ProtoHookOrder.ReportSinks;

    public async Task AfterRunAsync(CancellationToken cancellationToken = default)
    {
        var items = ProtoReportItems.Collect(collectors, reportSources);
        var exceptions = new List<Exception>();

        foreach (var sink in sinks)
        {
            try
            {
                await sink.ExportAsync(items, cancellationToken);
                if (sink is IProtoSinkArtifactSource artifactSource)
                {
                    await traceSession.CaptureRunArtifactsAsync(
                        artifactSource.GetArtifacts(),
                        sink.GetType().Name,
                        cancellationToken);
                }
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }
        }

        LifecycleExceptionHelper.ThrowIfAny("One or more report sinks failed to export.", exceptions);
    }
}

