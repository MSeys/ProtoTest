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
        context.RegisterClient(new ProtoMessageClient(context, broker, options, broker.Position), Name);
        return Task.FromResult(true);
    }
}
