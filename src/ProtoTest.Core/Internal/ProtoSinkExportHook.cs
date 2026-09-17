namespace ProtoTest.Core.Internal;

using System.Runtime.ExceptionServices;

internal sealed class ProtoSinkExportHook(
    IEnumerable<IProtoCollector> collectors,
    IEnumerable<IProtoReportSource> reportSources,
    IEnumerable<IProtoSink> sinks,
    ProtoTraceSession traceSession) : IProtoRunHook
{
    // AfterRun executes in descending order. Reports are generated after the gates and before the
    // run-scoped resources are released and the trace archive is written.
    public int Order => int.MinValue + 2;

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

        if (exceptions.Count == 1)
        {
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
        }

        if (exceptions.Count > 1)
        {
            throw new AggregateException("One or more report sinks failed to export.", exceptions);
        }
    }
}
