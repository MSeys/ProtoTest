namespace ProtoTest.Messaging.RabbitMq.Tests;

using System.Reflection;
using global::RabbitMQ.Client;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;
using ProtoTest.Messaging.RabbitMq.Testcontainers;

[TestFixture]
public sealed class RabbitMqTests
{
    private static RabbitMqBroker? _container;

    [OneTimeTearDown]
    public async Task StopContainer()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
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
        var context = await host.StartTestAsync("rabbit options", TestMethod());

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
        using (var bootstrap = Connect(connectionString))
        using (var channel = bootstrap.CreateModel())
        {
            channel.ExchangeDeclare(exchange, ExchangeType.Fanout, durable: false, autoDelete: true);
        }

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
            var context = await host.StartTestAsync("rabbit round trip", TestMethod());
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
            using var cleanup = Connect(connectionString);
            using var channel = cleanup.CreateModel();
            channel.ExchangeDelete(exchange);
        }
    }

    [Test]
    public async Task PublishAndAwait_ShouldRoundTripOnADirectExchange()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        using (var bootstrap = Connect(connectionString))
        using (var channel = bootstrap.CreateModel())
        {
            channel.ExchangeDeclare(exchange, ExchangeType.Direct, durable: false, autoDelete: true);
        }

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
            var context = await host.StartTestAsync("rabbit direct round trip", TestMethod());
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
            using var cleanup = Connect(connectionString);
            using var channel = cleanup.CreateModel();
            channel.ExchangeDelete(exchange);
        }
    }

    [Test]
    public async Task PublishAndAwait_ShouldRoundTripOnAHeadersExchange()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        using (var bootstrap = Connect(connectionString))
        using (var channel = bootstrap.CreateModel())
        {
            channel.ExchangeDeclare(exchange, ExchangeType.Headers, durable: false, autoDelete: true);
        }

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
            var context = await host.StartTestAsync("rabbit headers round trip", TestMethod());
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
            using var cleanup = Connect(connectionString);
            using var channel = cleanup.CreateModel();
            channel.ExchangeDelete(exchange);
        }
    }

    [Test]
    public async Task TwoConsumersAwaitingTheSameDestination_ShouldEachGetTheirOwnMessage()
    {
        var connectionString = RequireBroker();

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        using (var bootstrap = Connect(connectionString))
        using (var channel = bootstrap.CreateModel())
        {
            channel.ExchangeDeclare(exchange, ExchangeType.Fanout, durable: false, autoDelete: true);
        }

        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit consumer isolation", TestMethod());
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
            using var cleanup = Connect(connectionString);
            using var channel = cleanup.CreateModel();
            channel.ExchangeDelete(exchange);
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
        var context = await host.StartTestAsync("rabbit missing exchange", TestMethod());
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
        using (var bootstrap = Connect(connectionString))
        using (var channel = bootstrap.CreateModel())
        {
            channel.ExchangeDeclare(exchange, ExchangeType.Fanout, durable: false, autoDelete: true);
        }

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
                options.PollInterval = TimeSpan.FromMilliseconds(10);
            }));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit non matching stream", TestMethod());
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
            using var cleanup = Connect(connectionString);
            using var channel = cleanup.CreateModel();
            channel.ExchangeDelete(exchange);
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
        var context = await host.StartTestAsync("rabbit disposed consumer", TestMethod());
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
                "or start a container runtime.");
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

        if (_container is null && RabbitMqBroker.TryStart(configure: null, out var broker, out _))
        {
            _container = broker;
        }

        return _container?.ConnectionString;
    }

    private static IConnection Connect(string connectionString)
        => new ConnectionFactory { Uri = new Uri(connectionString) }
            .CreateConnection("ProtoTest.Messaging.RabbitMq.Tests");

    private static MethodInfo TestMethod()
        => typeof(RabbitMqTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
