namespace ProtoTest.Messaging;

using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Entry point for publishing and awaiting messages. The client is created during setup with a
    /// cursor at the broker's current position, so it only consumes what happens during this test.
    /// </summary>
    public static ProtoMessageClient Messages(this ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.TryClient<ProtoMessageClient>()
            ?? throw new InvalidOperationException(
                "Messaging is not composed for this host. Call AddMessaging on the host builder.");
    }
}
