namespace ProtoTest.Messaging.RabbitMq.Tests;

using System.Reflection;
using global::RabbitMQ.Client;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.RabbitMq;

[TestFixture]
public sealed class RabbitMqTests
{
    private const string DefaultConnection = "amqp://guest:guest@localhost:5672/";

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
    public async Task PublishAndAwait_ShouldRoundTripAgainstAConfiguredBroker()
    {
        if (!TryConnect(DefaultConnection))
        {
            Assert.Ignore(
                "No RabbitMQ broker on localhost:5672. Configure " +
                "'ProtoTest:Messaging:RabbitMq:ConnectionString' to run this against a real one.");
        }

        var exchange = $"prototest.tests.{Guid.NewGuid():N}";
        using (var bootstrap = Connect(DefaultConnection))
        using (var channel = bootstrap.CreateModel())
        {
            channel.ExchangeDeclare(exchange, ExchangeType.Fanout, durable: false, autoDelete: true);
        }

        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddMessaging(messaging => messaging.UseRabbitMq(options =>
                options.ConnectionString = DefaultConnection));
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("rabbit round trip", TestMethod());
            var messages = context.Messages();

            await messages.PublishAsync(exchange, "{\"id\":1}", contentType: "application/json");
            var received = await messages.AwaitAsync(
                exchange,
                message => message.Payload == "{\"id\":1}",
                TimeSpan.FromSeconds(10));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            Assert.That(received.ContentType, Is.EqualTo("application/json"));
        }
        finally
        {
            using var cleanup = Connect(DefaultConnection);
            using var channel = cleanup.CreateModel();
            channel.ExchangeDelete(exchange);
        }
    }

    private static bool TryConnect(string connectionString)
    {
        try
        {
            using var connection = Connect(connectionString);
            return true;
        }
        catch (Exception exception) when (exception is
            global::RabbitMQ.Client.Exceptions.BrokerUnreachableException or UriFormatException)
        {
            return false;
        }
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
