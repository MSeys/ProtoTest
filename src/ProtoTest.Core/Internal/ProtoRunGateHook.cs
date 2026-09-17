namespace ProtoTest.Core.Internal;

internal sealed class ProtoRunGateHook(
    IEnumerable<IProtoRunGate> gates,
    IEnumerable<IProtoCollector> collectors,
    IEnumerable<IProtoReportSource> reportSources,
    ProtoTraceSession trace) : IProtoRunHook
{
    // AfterRun executes in descending order. Gates run first so their findings reach the sinks, the
    // run-scoped resources are still alive, and the trace archive picks everything up.
    public int Order => int.MinValue + 3;

    public Task AfterRunAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gateList = gates.ToArray();
        if (gateList.Length == 0)
        {
            return Task.CompletedTask;
        }

        var context = new ProtoRunGateContext(ProtoReportItems.Collect(collectors, reportSources));
        var findings = new List<ProtoReportItem>(gateList.Length);
        var failures = new List<ProtoRunGateFailure>();

        foreach (var gate in gateList)
        {
            ProtoRunGateResult result;
            try
            {
                result = gate.Evaluate(context)
                    ?? throw new InvalidOperationException($"Run gate '{gate.Name}' returned no result.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = ProtoRunGateResult.Failed(
                    $"The gate threw {exception.GetType().Name}: {exception.Message}");
            }

            findings.Add(new ProtoReportItem(
                "Run gates",
                "Gate",
                gate.Name,
                Kind: ProtoReportItemKind.Finding,
                Status: StatusOf(result.Outcome),
                Count: 1,
                Message: result.Message,
                Tags: result.Details));

            // A gate judges the run, so it belongs to the run's trace, not to any test's.
            trace.RunWriter.WriteEvent(
                "gate.evaluate",
                $"Gate · {gate.Name}",
                "ProtoTest.Core",
                ProtoTracePhase.Run,
                OutcomeOf(result.Outcome),
                new Dictionary<string, string?>
                {
                    ["gate.name"] = gate.Name,
                    ["gate.status"] = result.Outcome.ToString(),
                    ["gate.message"] = result.Message,
                    ["gate.details"] = result.Details is null ? null : string.Join(", ", result.Details)
                });

            if (result.Outcome == ProtoRunGateOutcome.Failed)
            {
                failures.Add(new ProtoRunGateFailure(gate.Name, result.Message, result.Details ?? []));
            }
        }

        reportSources.OfType<ProtoRunGateReportSource>().FirstOrDefault()?.Publish(findings);

        return failures.Count == 0
            ? Task.CompletedTask
            : Task.FromException(new ProtoRunGateException(failures));
    }

    private static ProtoReportStatus StatusOf(ProtoRunGateOutcome outcome) => outcome switch
    {
        ProtoRunGateOutcome.Passed => ProtoReportStatus.Success,
        ProtoRunGateOutcome.Warning => ProtoReportStatus.Warning,
        ProtoRunGateOutcome.Failed => ProtoReportStatus.Error,
        _ => ProtoReportStatus.Neutral
    };

    private static ProtoTraceOutcome OutcomeOf(ProtoRunGateOutcome outcome) => outcome switch
    {
        ProtoRunGateOutcome.Passed => ProtoTraceOutcome.Succeeded,
        ProtoRunGateOutcome.Warning => ProtoTraceOutcome.Partial,
        ProtoRunGateOutcome.Failed => ProtoTraceOutcome.Failed,
        _ => ProtoTraceOutcome.Skipped
    };
}
