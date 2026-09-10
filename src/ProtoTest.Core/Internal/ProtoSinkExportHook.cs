namespace ProtoTest.Core;

using System.Runtime.ExceptionServices;

internal sealed class ProtoSinkExportHook(
    IEnumerable<IProtoCollector> collectors,
    IEnumerable<IProtoReportSource> reportSources,
    IEnumerable<IProtoSink> sinks) : IProtoRunHook
{
    // AfterRun executes in descending order. A low order exports after other hooks finalize their data.
    public int Order => int.MinValue;

    public async Task AfterRunAsync(CancellationToken cancellationToken = default)
    {
        var items = collectors.OfType<IProtoReportSource>()
            .Concat(reportSources)
            .Distinct<IProtoReportSource>(ReferenceEqualityComparer.Instance)
            .SelectMany(source => source.GetReportItems())
            .OrderBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exceptions = new List<Exception>();

        foreach (var sink in sinks)
        {
            try
            {
                await sink.ExportAsync(items, cancellationToken);
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
