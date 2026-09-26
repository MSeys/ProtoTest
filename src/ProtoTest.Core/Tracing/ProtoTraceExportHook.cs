namespace ProtoTest.Core;

using ProtoTest.Core.Internal;

internal sealed class ProtoTraceExportHook(
    ProtoTraceSession traceSession,
    ProtoTraceOptions options) : IProtoRunHook, IProtoRunEvidenceHook
{
    // AfterRun executes in descending order. Trace export runs last so generated sink artifacts can be bundled.
    public int Order => ProtoHookOrder.First;

    public Task BeforeRunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AfterRunAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Enabled) return Task.CompletedTask;
        var run = traceSession.Snapshot();
        var path = options.OutputPath
            ?? Path.Combine("TestResults", $"prototest-{run.RunId}.prototrace");
        return ProtoTraceArchiveWriter.WriteAsync(
            path,
            run,
            traceSession.SnapshotArtifactSources(),
            options.CaptureSourceLocations && options.EmbedSources,
            cancellationToken);
    }
}

