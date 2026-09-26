namespace ProtoTest.Json;

using ProtoTest.Core;

/// <summary>
/// Records a diagnostic observation without ever hiding the failure that produced it: a failure while
/// building diagnostics is traced as evidence and swallowed, because the original exception is what the
/// caller must see. The protocol's identity supplies the event kind, name and source, so a GraphQL
/// capture failure is never attributed to HTTP and every protocol traces the same way.
/// </summary>
public static class ProtoObservationCapture
{
    public static void TryRecord(
        ProtoExecutionContext context,
        ProtoProtocol protocol,
        Func<ProtoObservation> observation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(observation);
        try
        {
            context.RecordObservation(observation());
        }
        catch (Exception exception)
        {
            context.Trace.WriteEvent(
                $"{protocol.Key.ToLowerInvariant()}.diagnostics.failed",
                $"{protocol.Name} diagnostic capture failed",
                protocol.TraceSource,
                outcome: ProtoTraceOutcome.Failed,
                exception: exception);
        }
    }
}
