namespace ProtoTest.Messaging.RabbitMq.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Messaging;

/// <summary>
/// Pins the Broker capability's address rule: <c>UseRabbitMq</c> declares it only while
/// the connection-string key can be provided - configured, or declared by a registered broker
/// container - so a run with neither skips instead of failing at setup or first publish.
/// </summary>
[TestFixture]
public sealed class ProvidedAddressCapabilityTests
{
    private const string ConnectionStringKey = RabbitMqOptions.ConnectionStringSetting;

    [Test]
    public async Task UseRabbitMq_WhenTheConnectionStringIsConfigured_ShouldKeepTheBrokerCapability()
    {
        var builder = BuilderWith();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [ConnectionStringKey] = "amqp://guest:guest@broker:5672/"
            }));
        builder.AddMessaging(messaging => messaging.UseRabbitMq());

        await using var host = builder.Build();

        Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
    }

    [Test]
    public async Task UseRabbitMq_WhenInfrastructureDeclaresTheKey_ShouldKeepTheBrokerCapability()
    {
        var builder = BuilderWith();
        builder.AddInfrastructure(
            new DeclaredSettingsInfrastructure("broker:rabbitmq", ConnectionStringKey, "amqp://guest:guest@container:5672/"),
            ConnectionStringKey);
        builder.AddMessaging(messaging => messaging.UseRabbitMq());

        await using var host = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(host.Configuration[ConnectionStringKey], Is.Null, "the key has no configured value");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Broker),
                Is.True,
                "a broker container that declares the key keeps the capability before it starts");
        });
    }

    [Test]
    public async Task UseRabbitMq_WhenNeitherIsProvided_ShouldDropTheBrokerCapabilityAndNameTheKey()
    {
        var output = Path.Combine(Path.GetTempPath(), $"prototest-broker-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.AddMessaging(messaging => messaging.UseRabbitMq());
            await using var host = builder.Build();
            await host.StartAsync();

            var skipped = host.Trace.Snapshot().Entries!.Single(entry => entry.Kind == "capability.skipped");
            await host.StopAsync();

            Assert.Multiple(() =>
            {
                Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.False);
                Assert.That(skipped.Attributes["capability.keys"], Does.Contain(ConnectionStringKey));
                Assert.That(skipped.Attributes["capability.reason"], Is.EqualTo("no key provided"));
            });
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task UseRabbitMq_WhenTheCallbackProvidesTheConnectionString_ShouldKeepTheBrokerCapability()
    {
        var builder = BuilderWith();
        builder.AddMessaging(messaging => messaging.UseRabbitMq(
            options => options.ConnectionString = "amqp://guest:guest@127.0.0.1:5672/"));

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(ProtoCapabilityKinds.Broker),
            Is.True,
            "a code-provided address cannot be withdrawn by configuration");
    }

    [Test]
    public async Task UseRabbitMq_WhenDeclarationsExistButNoAddressIsProvided_ShouldStillDropTheBrokerCapability()
    {
        var builder = BuilderWith();
        builder.AddMessaging(messaging => messaging.Declare("suite.owned").UseRabbitMq());

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(ProtoCapabilityKinds.Broker),
            Is.False,
            "a declaration changes no address rule: without a configured key or a container there is no broker to declare on");
    }

    [Test]
    public async Task AddMessaging_WithoutAnAdapter_ShouldNotRegisterTheBrokerCapability()
    {
        var builder = BuilderWith();
        builder.AddMessaging();

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(ProtoCapabilityKinds.Broker),
            Is.False,
            "the in-memory default is a test double, not a broker capability");
    }

    private static ProtoHostBuilder BuilderWith()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        return builder;
    }
}
