namespace ProtoTest.Messaging.MassTransit.Tests;

using global::MassTransit;
using global::MassTransit.RabbitMqTransport;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.Messaging.RabbitMq.Testcontainers;
using ProtoTest.TestSupport;

/// <summary>
/// The envelope interop proved against a real broker: a MassTransit bus with the RabbitMQ transport
/// owns the consumer, and the suite reaches it through the RabbitMQ adapter - the closed-box path a
/// published application is tested by. The bus's own topology declares the exchanges (its publish
/// side, and the consumer endpoint for each contract), so the adapter binds and publishes to what the
/// application already declared. Tests get unique run ids and endpoint names, so they isolate on the
/// shared container; a run without a configured broker and without a container runtime skips.
/// </summary>
[TestFixture]
public sealed class MassTransitEnvelopeBrokerTests
{
    // One lazy start shared by the parallel tests, like the RabbitMQ suite's fixture.
    private static readonly Lazy<(RabbitMqBroker? Broker, string? Error)> Container = new(
        static () =>
        {
            var result = RabbitMqBroker.TryStart();
            return (result.Resource, result.Error);
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    [OneTimeTearDown]
    public static async Task StopContainer()
    {
        if (Container.IsValueCreated && Container.Value.Broker is { } container)
        {
            await container.DisposeAsync();
        }
    }

    [Test]
    public async Task Wrap_ShouldReachAMassTransitConsumerOverRabbitMq()
    {
        var connectionString = RequireBroker();
        var runId = Guid.NewGuid();
        var received = new TaskCompletionSource<ConsumedEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bus = CreateBus(connectionString, runId, received);
        await bus.StartAsync();
        try
        {
            var destination = DestinationOf<EnvelopeProbeCommand>();
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.Enabled = false);
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("masstransit envelope publish", TestMethods.Placeholder);

            var frame = MassTransitEnvelope.Wrap(
                destination,
                new EnvelopeProbeCommand(runId, 42, 10.5m),
                correlationId: runId,
                headers: new Dictionary<string, string?> { ["tenant"] = "northstar" });
            await context.Messaging().PublishAsync(frame.Destination, frame.Payload, frame.Headers, frame.ContentType);

            var consumed = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(consumed.Message.RunId, Is.EqualTo(runId));
                Assert.That(consumed.Message.InvoiceId, Is.EqualTo(42));
                Assert.That(consumed.Message.Amount, Is.EqualTo(10.5m));
                Assert.That(consumed.CorrelationId, Is.EqualTo(runId));
                Assert.That(consumed.Tenant, Is.EqualTo("northstar"));
            }
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    [Test]
    public async Task Unwrap_ShouldReadAnEventTheMassTransitBusPublishedOverRabbitMq()
    {
        var connectionString = RequireBroker();
        var runId = Guid.NewGuid();
        var bus = CreateBus(connectionString, runId, received: null);
        await bus.StartAsync();
        try
        {
            var destination = DestinationOf<EnvelopeProbeEvent>();
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.Enabled = false);
            builder.AddMessaging(messaging => messaging
                .Tap(destination)
                .UseRabbitMq(options => options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("masstransit envelope await", TestMethods.Placeholder);

            await bus.Publish(new EnvelopeProbeEvent(runId, 42, 10.5m));
            var frame = await context.Messaging().AwaitAsync(
                destination,
                message => message.Payload!.Contains(runId.ToString()),
                TimeSpan.FromSeconds(30));

            var content = MassTransitEnvelope.Unwrap(frame);
            var @event = MassTransitEnvelope.Unwrap<EnvelopeProbeEvent>(frame);
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(frame.ContentType, Is.EqualTo(MassTransitEnvelope.ContentType));
                Assert.That(@event, Is.EqualTo(new EnvelopeProbeEvent(runId, 42, 10.5m)));
                Assert.That(content.MessageTypes, Does.Contain(UrnOf<EnvelopeProbeEvent>()));
                Assert.That(content.MessageId, Is.Not.Null);
                Assert.That(content.ConversationId, Is.Not.Null);
                Assert.That(content.SentTime, Is.Not.Null);
            }
        }
        finally
        {
            await bus.StopAsync();
        }
    }

    private static string RequireBroker()
    {
        var connectionString = ResolveBroker();
        if (connectionString is null)
        {
            Assert.Ignore(
                "No RabbitMQ broker is available: set ProtoTest__Messaging__RabbitMq__ConnectionString " +
                "or start a container runtime. " + Container.Value.Error);
        }

        return connectionString;
    }

    private static string? ResolveBroker()
    {
        var configured = Environment.GetEnvironmentVariable("ProtoTest__Messaging__RabbitMq__ConnectionString");
        return string.IsNullOrWhiteSpace(configured) ? Container.Value.Broker?.ConnectionString : configured;
    }

    /// <summary>
    /// Starts a bus with the RabbitMQ transport on the run's broker: the consumer endpoint declares the
    /// command's exchange and receives on it, and a no-op event consumer declares the event's exchange
    /// the same way an application that publishes an event also consumes it.
    /// </summary>
    private static IBusControl CreateBus(string connectionString, Guid runId, TaskCompletionSource<ConsumedEnvelope>? received)
        => Bus.Factory.CreateUsingRabbitMq(configuration =>
        {
            configuration.Host(new Uri(connectionString));
            configuration.ReceiveEndpoint($"prototest-mt-envelope-{runId:N}", endpoint =>
            {
                if (received is not null)
                {
                    endpoint.Consumer(() => new EnvelopeProbeCommandConsumer(runId, received));
                }

                endpoint.Consumer(() => new EnvelopeProbeEventConsumer());
            });
        });

    /// <summary>The exchange MassTransit names after a contract, which the adapter must publish to.</summary>
    private static string DestinationOf<T>() => new RabbitMqMessageNameFormatter().GetMessageName(typeof(T));

    private static string UrnOf<T>() => $"urn:message:{typeof(T).Namespace}:{typeof(T).Name}";
}
