namespace ProtoTest.Messaging.RabbitMq.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Messaging;

/// <summary>
/// The broker address is validated where the options resolve, so a malformed value fails naming its
/// configuration key instead of at connect time.
/// </summary>
[TestFixture]
public sealed class RabbitMqOptionsValidationTests
{
    [Test]
    public void Validate_ShouldRejectAMalformedConnectionStringNamingTheKey()
    {
        var options = new RabbitMqOptions { ConnectionString = "not-a-uri" };

        var exception = Assert.Throws<ArgumentException>(() => options.Validate());

        Assert.That(exception!.Message, Does.Contain(RabbitMqOptions.ConnectionStringSetting));
    }

    [Test]
    public void Validate_ShouldRejectANonAmqpScheme()
    {
        var options = new RabbitMqOptions { ConnectionString = "http://guest:guest@broker:15672/" };

        var exception = Assert.Throws<ArgumentException>(() => options.Validate());

        Assert.That(exception!.Message, Does.Contain("amqp"));
    }

    [Test]
    public void Validate_ShouldAcceptAnAbsoluteAmqpsUri()
    {
        var options = new RabbitMqOptions { ConnectionString = "amqps://guest:guest@broker:5671/" };

        Assert.DoesNotThrow(() => options.Validate());
    }

    [Test]
    public async Task UseRabbitMq_WhenTheConfiguredConnectionStringIsMalformed_ShouldFailAtResolveNamingTheKey()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [RabbitMqOptions.ConnectionStringSetting] = "not-a-uri"
            }));
        builder.AddMessaging(messaging => messaging.UseRabbitMq());

        await using var host = builder.Build();
        await host.StartAsync();

        // Test setup resolves the broker, which resolves and validates the options.
        var exception = Assert.ThrowsAsync<ArgumentException>(
            () => host.StartTestAsync("malformed broker", TestMethods.Placeholder));

        Assert.That(exception!.Message, Does.Contain(RabbitMqOptions.ConnectionStringSetting));
        await host.StopAsync();
    }
}
