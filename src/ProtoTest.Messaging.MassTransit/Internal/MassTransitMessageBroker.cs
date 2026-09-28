namespace ProtoTest.Messaging.MassTransit.Internal;

using global::MassTransit;
using global::MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Messaging;

/// <summary>
/// The MassTransit adapter: the framework's broker seam over the application's in-process
/// <see cref="ITestHarness"/>. Publishing goes through the harness bus, so the application's own
/// consumers receive the message; awaiting observes what the bus published, so the messages the
/// application publishes through its own <c>IPublishEndpoint</c> are the ones a test awaits. The
/// harness owns its transport, topology and consumers - the bridge never reimplements them.
/// </summary>
internal sealed class MassTransitMessageBroker<TProgram>(string application) : IProtoMessageBroker
    where TProgram : class
{
    private readonly string _application = application;

    /// <summary>The adapter name recorded on the capability, the broker entity and the trace.</summary>
    public string Name => "MassTransit";

    /// <summary>
    /// Publishes one message over the harness bus. The destination names the message contract type and
    /// the payload is JSON for it, so the application's consumers receive the message they are
    /// configured for; the message's headers ride the publish context.
    /// </summary>
    public async ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.RoutingKey is not null)
        {
            throw new InvalidOperationException(
                $"Cannot publish to '{message.Destination}' with routing key '{message.RoutingKey}' through " +
                "MassTransit: a MassTransit destination is a message contract type, and the bus owns its " +
                "transport's routing. Publish without a routing key, or use a broker adapter whose transport " +
                "carries one (the RabbitMQ adapter).");
        }

        var harness = Harness();
        var contractType = MassTransitMessages.Resolve(message.Destination, typeof(TProgram).Assembly);
        if (contractType.IsAbstract || contractType.IsInterface || contractType.IsGenericTypeDefinition)
        {
            throw new InvalidOperationException(
                $"The destination '{message.Destination}' names {contractType.Name}, which is not a concrete message " +
                "contract. MassTransit publishes an instance, so the test can only publish a concrete contract type; " +
                "an interface contract can be awaited, but it must be published by the application.");
        }

        var instance = MassTransitMessages.Deserialize(message.Destination, contractType, message.Payload);
        var headers = message.Headers?
            .Where(header => header.Value is not null)
            .ToArray() ?? [];
        if (headers.Length == 0)
        {
            await harness.Bus.Publish(instance, contractType, cancellationToken).ConfigureAwait(false);
            return;
        }

        await harness.Bus.Publish(
            instance,
            contractType,
            Pipe.ExecuteAsync<PublishContext>(context =>
            {
                foreach (var (key, value) in headers)
                {
                    context.Headers.Set(key, value!);
                }

                return Task.CompletedTask;
            }),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates one test's consumer. It snapshots the harness's published position at its first use, so
    /// only messages published after the test prepared can match; the harness itself keeps history for
    /// the whole run.
    /// </summary>
    public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
        => new(new MassTransitProtoMessageConsumer<TProgram>(this));

    /// <summary>
    /// A declaration is a no-op: MassTransit owns message topology, and every destination - a message
    /// contract type - already exists on the bus or is created by it on first use, exactly like the
    /// in-memory broker whose destinations always exist.
    /// </summary>
    public ValueTask DeclareAsync(
        IReadOnlyCollection<string> destinations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destinations);
        return ValueTask.CompletedTask;
    }

    /// <summary>Resolves the harness for the running test.</summary>
    internal ITestHarness Harness()
    {
        var context = ProtoHost.CurrentContextOrNull
            ?? throw new InvalidOperationException(
                "The MassTransit broker runs inside a test: publishing, preparing and awaiting need an active " +
                "ProtoTest execution context.");

        var factory = context.TryServerFactory<TProgram>(_application);
        if (factory is null)
        {
            var address = ProtoApplication.BaseUrl(context, _application);
            throw new InvalidOperationException(
                address is null
                    ? $"Application '{_application}' has no in-process ASP.NET Core server, so its MassTransit test " +
                        $"harness cannot be reached. Register the application with AddAspNetCoreServer<{typeof(TProgram).Name}>" +
                        $"(\"{_application}\") and call AddMessaging after it, so the messaging client initializes once the server exists."
                    : $"Application '{_application}' runs at '{address}', so its MassTransit test harness is not in this " +
                        "process. The MassTransit bridge serves the in-process test harness only; gate tests that need it " +
                        "with [RequiresCapability(ProtoCapabilityKinds.Broker)].");
        }

        return factory.Services.GetService<ITestHarness>()
            ?? throw new InvalidOperationException(
                $"Application '{_application}' has no MassTransit test harness registered. Compose the application with " +
                "AddMassTransitTestHarness() so the suite can reach ITestHarness.");
    }
}
