namespace ProtoTest.Messaging;

using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Entry point for publishing and awaiting messages. The client and its per-test consumer are created
    /// during setup, so the test only consumes messages that arrive during this test.
    /// </summary>
    public static ProtoMessageClient Messaging(this ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryClient<ProtoMessageClient>()
            ?? throw new InvalidOperationException(
                "Messaging is not composed for this host. Call AddMessaging on the host builder.");
    }
}
