namespace ProtoTest.Http;

using ProtoTest.Core;

/// <summary>
/// Records a diagnostic observation without ever hiding the failure that produced it: a failure while
/// building diagnostics is swallowed, because the original exception is what the caller must see.
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
        catch
        {
            // A diagnostic failure must never hide the original request failure.
        }
    }
}
