namespace ProtoTest.Messaging.RabbitMq.Tests;

using System.Reflection;
using global::RabbitMQ.Client;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.Messaging.RabbitMq.Testcontainers;

[TestFixture]
public sealed class RabbitMqTests
{
    // One lazy start shared by every parallel test: a check-then-act here would start a container
    // per racing caller and leak all but the last.
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
    public async Task UseRabbitMq_ShouldRegisterTheCapabilityAndBindConfiguration()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Messaging:RabbitMq:ConnectionString"] = "amqp://127.0.0.1:1/"
            }));
        builder.AddMessaging(messaging => messaging.UseRabbitMq());
        await using var host = builder.Build();
        Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
        await host.StartAsync();
        var context = await host.StartTestAsync("rabbit options", TestMethods.Placeholder);

        var options = context.Service<RabbitMqOptions>();
        var unreachable = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().PublishAsync("invoices", "{}"));

        await host.CompleteTestAsync(ProtoTestResult.Failed(unreachable!));
        Assert.Multiple(() =>
        {
            Assert.That(options.ConnectionString, Is.EqualTo("amqp://127.0.0.1:1/"));
            Assert.That(unreachable!.Message, Does.Contain("ProtoTest:Messaging:RabbitMq:ConnectionString"));
        });
    }

    [Test]
    public async Task PublishAndAwait_ShouldRoundTripAgainstARealBroker()
    {
        // A configured broker wins; otherwise the test owns a container. A machine with neither skips
        // instead of passing against the in-memory double.
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    // The test's tap queue is declared at setup; the exchange exists before the publish.
                    ["ProtoTest:Messaging:Destinations:0"] = exchange
                }));
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit round trip", TestMethods.Placeholder);
            var messages = context.Messaging();

            await messages.PublishAsync(exchange, "{\"id\":1}", contentType: "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.ContentType, Is.EqualTo("application/json"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Tap_ShouldPreBindTheDestinationSoAnEarlierPublishIsReceived()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout);

        try
        {
            // The tap is declared in code, so the consumer prepares it during test setup: the message
            // published before the test's first await is still delivered to this test's queue.
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging
                .UseRabbitMq(options => options.ConnectionString = connectionString)
                .Tap(exchange));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit tap prebind", TestMethods.Placeholder);
            var messages = context.Messaging();

            await messages.PublishAsync(exchange, "{\"id\":1}", contentType: "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.ContentType, Is.EqualTo("application/json"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task PublishAndAwait_ShouldRoundTripOnADirectExchange()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Direct);

        try
        {
            // The tap binds the destination routing key as well as "#", which is what makes a direct
            // exchange round trip while a topic exchange stays catch-all.
            var builder = new ProtoHostBuilder();
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Messaging:Destinations:0"] = exchange
                }));
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit direct round trip", TestMethods.Placeholder);
            var messages = context.Messaging();

            await messages.PublishAsync(exchange, "{\"id\":1}", contentType: "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.Payload, Is.EqualTo("{\"id\":1}"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task PublishAndAwait_ShouldRoundTripOnAHeadersExchange()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Headers);

        try
        {
            // A headers exchange ignores the routing key; the tap's argument-less bindings match every
            // message, so the same act-then-await flow works without knowing the application's headers.
            var builder = new ProtoHostBuilder();
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Messaging:Destinations:0"] = exchange
                }));
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit headers round trip", TestMethods.Placeholder);
            var messages = context.Messaging();

            await messages.PublishAsync(
                exchange,
                "{\"id\":1}",
                new Dictionary<string, string?> { ["event"] = "invoice.paid" },
                "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.Headers!["event"], Is.EqualTo("invoice.paid"));
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task TwoConsumersAwaitingTheSameDestination_ShouldEachGetTheirOwnMessage()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit consumer isolation", TestMethods.Placeholder);
            var broker = context.Service<IProtoMessageBroker>();
            var messages = context.Messaging();

            // Two tests, simulated by two consumers created in order: each has its own queue, so the
            // second never sees the first's message even though both await the same exchange.
            var first = await broker.CreateConsumerAsync();
            context.RegisterResource("messaging:consumer:first", "consumer", "First consumer", _ => first.DisposeAsync());
            await first.PrepareAsync([exchange]);
            await messages.PublishAsync(exchange, "{\"id\":1}");

            var second = await broker.CreateConsumerAsync();
            context.RegisterResource("messaging:consumer:second", "consumer", "Second consumer", _ => second.DisposeAsync());
            await second.PrepareAsync([exchange]);
            await messages.PublishAsync(exchange, "{\"id\":2}");

            var firstOwn = await first.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(15));
            var secondOwn = await second.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":2}",
                TimeSpan.FromSeconds(15));
            var stolen = Assert.ThrowsAsync<TimeoutException>(async () =>
                await second.AwaitAsync(
                    exchange,
                    message => message.Payload == "{\"id\":1}",
                    TimeSpan.FromSeconds(1)));

            await host.CompleteTestAsync(ProtoTestResult.Failed(stolen!));
            Assert.Multiple(() =>
            {
                Assert.That(firstOwn.Payload, Is.EqualTo("{\"id\":1}"));
                Assert.That(secondOwn.Payload, Is.EqualTo("{\"id\":2}"));
                Assert.That(stolen!.Message, Does.Contain(exchange));
            });
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Prepare_ShouldReportAMissingExchange()
    {
        var connectionString = RequireBroker();

        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
            options.ConnectionString = connectionString));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rabbit missing exchange", TestMethods.Placeholder);
        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        var consumer = await context.Service<IProtoMessageBroker>().CreateConsumerAsync();
        context.RegisterResource("messaging:consumer:missing", "consumer", "Missing exchange consumer",
            _ => consumer.DisposeAsync());

        var missing = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await consumer.PrepareAsync([exchange]));

        await host.CompleteTestAsync(ProtoTestResult.Failed(missing!));
        Assert.Multiple(() =>
        {
            Assert.That(missing!.Message, Does.Contain(exchange));
            Assert.That(missing.Message, Does.Contain("does not exist"));
        });
    }

    [Test]
    public async Task Await_ShouldTimeOutWhileNonMatchingMessagesKeepArriving()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        await DeclareExchangeAsync(connectionString, exchange, ExchangeType.Fanout);

        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Messaging:Destinations:0"] = exchange
                }));
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
            {
                options.ConnectionString = connectionString;

            }));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit non matching stream", TestMethods.Placeholder);
            var messages = context.Messaging();

            // A backlog that would take seconds to drain at one slow predicate per message: the deadline
            // must win instead of the loop spinning until the queue finally empties.
            for (var index = 0; index < 200; index++)
            {
                await messages.PublishAsync(exchange, $"{{\"id\":{index}}}");
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var timeout = Assert.ThrowsAsync<TimeoutException>(async () =>
                await messages.AwaitAsync(
                    exchange,
                    message =>
                    {
                        // A deliberate timing probe: the predicate must outlast the poll
                        // interval so the wait is still pending when its timeout elapses.
                        Thread.Sleep(20);
                        return message.Payload == "never";
                    },
                    TimeSpan.FromMilliseconds(300)));
            stopwatch.Stop();

            await host.CompleteTestAsync(ProtoTestResult.Failed(timeout!));
            Assert.Multiple(() =>
            {
                Assert.That(timeout!.Message, Does.Contain(exchange));
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)),
                    "the deadline is checked on every iteration, not only when the queue is empty");
            });
        }
        finally
        {
            await DeleteExchangeAsync(connectionString, exchange);
        }
    }

    [Test]
    public async Task Await_AfterDispose_ShouldThrowObjectDisposedException()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Messaging:RabbitMq:ConnectionString"] = "amqp://127.0.0.1:1/"
            }));
        builder.AddMessaging(messaging => messaging.UseRabbitMq());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("rabbit disposed consumer", TestMethods.Placeholder);
        var consumer = await context.Service<IProtoMessageBroker>().CreateConsumerAsync();
        await consumer.DisposeAsync();

        var exception = Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await consumer.AwaitAsync("invoices", _ => true, TimeSpan.FromSeconds(1)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
        Assert.That(exception!.ObjectName, Does.Contain("RabbitMqProtoMessageConsumer"));
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
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return Container.Value.Broker?.ConnectionString;
    }

    /// <summary>Declares a throwaway exchange for one test, on a connection of its own.</summary>
    private static async Task DeclareExchangeAsync(string connectionString, string exchange, string type)
    {
        await using var connection = await ConnectAsync(connectionString);
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(exchange, type, durable: false, autoDelete: true);
    }

    /// <summary>Deletes the throwaway exchange, so a rerun starts from the same broker state.</summary>
    private static async Task DeleteExchangeAsync(string connectionString, string exchange)
    {
        await using var connection = await ConnectAsync(connectionString);
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeleteAsync(exchange);
    }

    private static async Task<IConnection> ConnectAsync(string connectionString)
        => await new ConnectionFactory { Uri = new Uri(connectionString) }
            .CreateConnectionAsync("ProtoTest.Messaging.RabbitMq.Tests");
}
