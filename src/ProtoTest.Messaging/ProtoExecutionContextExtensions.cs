namespace ProtoTest.Messaging;

using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Entry point for publishing and awaiting messages. The client and its per-test consumer are created
    /// during setup, so the test only consumes messages that arrive during this test. Without a
    /// <paramref name="name"/> the run's broker client is used; a name is only meaningful when an adapter
    /// registered a client under it, and an unknown name fails naming <c>AddMessaging</c>.
    /// </summary>
    public static ProtoMessageClient Messaging(this ProtoExecutionContext context, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var client = name is null
            ? context.TryClient<ProtoMessageClient>()
            : context.TryClient<ProtoMessageClient>(name);
        if (client is not null)
        {
            return client;
        }

        if (name is null)
        {
            throw new InvalidOperationException(
                "Messaging is not composed for this host. Call AddMessaging on the host builder.");
        }

        throw new InvalidOperationException(
            $"No messaging client named '{name}' is registered. AddMessaging registers the run's broker " +
            "client under 'Default'; call Messaging() or Messaging(\"Default\") for it, and pass a name " +
            "only when an adapter registered that name.");
    }
}
