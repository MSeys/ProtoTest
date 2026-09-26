namespace ProtoTest.Core.Internal;

internal sealed class ProtoSinkExportHook(
    IEnumerable<IProtoCollector> collectors,
    IEnumerable<IProtoReportSource> reportSources,
    IEnumerable<IProtoSink> sinks,
    ProtoTraceSession traceSession) : IProtoRunHook, IProtoRunEvidenceHook
{
    // AfterRun executes in descending order. Reports are generated after the gates and before the
    // run-scoped resources are released and the trace archive is written.
    public int Order => ProtoHookOrder.ReportSinks;

    public async Task AfterRunAsync(CancellationToken cancellationToken = default)
    {
        var sinkList = sinks.ToArray();
        if (sinkList.Length == 0)
        {
            // Nothing to export to: collecting would only re-run every report source.
            return;
        }

        var items = ProtoReportItems.Collect(collectors, reportSources);
        var exceptions = new List<Exception>();

        foreach (var sink in sinkList)
        {
            try
            {
                await sink.ExportAsync(items, cancellationToken);
                // Artifacts are only captured when they will be archived; with tracing off the bytes
                // would be read and retained for a run that writes nothing.
                if (traceSession.Enabled && sink is IProtoSinkArtifactSource artifactSource)
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

