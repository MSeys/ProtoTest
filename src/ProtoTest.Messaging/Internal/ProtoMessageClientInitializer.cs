namespace ProtoTest.Messaging.Internal;

using ProtoTest.Core;

/// <summary>
/// Creates the test's message client during setup and registers it as a test-scoped client, so it is
/// released with the test. The client snapshots the broker position at setup: it only ever consumes
/// messages published after the test started, so a shared deployed broker cannot leak another test's
/// messages into this one.
/// </summary>
internal sealed class ProtoMessageClientInitializer(string name) : IProtoClientInitializer<ProtoMessageClient>
{
    public string Name { get; } = name;

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var broker = context.Service<IProtoMessageBroker>();
        var options = context.Service<ProtoMessagingOptions>();
        if (broker is IProtoMessageBrokerSetup setup && options.Destinations.Count > 0)
        {
            // Bind the test's taps before it acts: a message published after this point is never missed.
            setup.Prepare([.. options.Destinations]);
        }

        context.RegisterClient(new ProtoMessageClient(context, broker, options, broker.Position), Name);
        return Task.FromResult(true);
    }
}
