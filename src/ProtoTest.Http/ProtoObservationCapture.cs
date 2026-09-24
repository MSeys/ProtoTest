namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// Records a diagnostic observation without ever hiding the failure that produced it: a failure while
/// building diagnostics is traced as evidence and swallowed, because the original exception is what the
/// caller must see and every protocol traces the same way.
/// </summary>
public static class ProtoObservationCapture
{
    public static void TryRecord(ProtoExecutionContext context, Func<ProtoObservation> observation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(observation);
        try
        {
            context.RecordObservation(observation());
        }
        catch (Exception exception)
        {
            context.Trace.WriteEvent(
                "http.diagnostics.failed",
                "HTTP diagnostic capture failed",
                "ProtoTest.Http",
                outcome: ProtoTraceOutcome.Failed,
                exception: exception);
        }
    }
}
