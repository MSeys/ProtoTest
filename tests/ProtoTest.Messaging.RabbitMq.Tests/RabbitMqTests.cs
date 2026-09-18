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

        var options = context.Service<ProtoRabbitMqOptions>();
        var unreachable = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messages().PublishAsync("invoices", "{}"));

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
        // A configured broker wins; otherwise the test owns a container, exactly like the demo. A
        // machine with neither skips instead of passing against the in-memory double.
        var connectionString = ResolveBroker();
        if (connectionString is null)
        {
            Assert.Ignore(
                "No RabbitMQ broker is available: set ProtoTest__Messaging__RabbitMq__ConnectionString " +
                "or start a container runtime.");
        }

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
                    // The test owns the tap for this exchange; it exists before the publish below.
                    ["ProtoTest:Messaging:Destinations:0"] = exchange
                }));
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = connectionString));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit round trip", TestMethod());
            var messages = context.Messages();

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
