namespace ProtoTest.Messaging.Internal;

using ProtoTest.Core;

/// <summary>
/// Creates the test's message client and its consumer during setup and registers both with the test, so
/// they are released with it. The consumer is test-owned: it snapshots history or declares its taps at
/// setup, and its disposal (registered as a test resource) deletes whatever the adapter created for this
/// test, so a shared deployed broker cannot leak another test's messages into this one.
/// </summary>
internal sealed class ProtoMessageClientInitializer(string name) : IProtoClientInitializer<ProtoMessageClient>
{
    public string Name { get; } = name;

    public async Task<bool> TryInitializeAsync(
        ProtoExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var broker = context.Service<IProtoMessageBroker>();
        var options = context.Service<MessagingOptions>();
        var consumer = await broker.CreateConsumerAsync(cancellationToken);

        // Register before preparing: a failed PrepareAsync must not leak the consumer's channel or queues.
        context.RegisterResource(
            $"messaging:consumer:{Name}",
            "consumer",
            $"Messaging consumer '{Name}'",
            _ => consumer.DisposeAsync());
        if (options.Destinations.Count > 0)
        {
            // Bind the test's taps before it acts: a message published after this point is never missed.
            await consumer.PrepareAsync([.. options.Destinations], cancellationToken);
        }

        context.RegisterClient(new ProtoMessageClient(context, broker, consumer, options), Name);
        return true;
    }
}
