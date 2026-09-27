namespace ProtoTest.Messaging.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Pins the seam's other address direction: <c>UseBrokerUnlessConfigured</c> declares the Broker
/// capability only while the named keys are not configured, so an in-process adapter whose address
/// being configured means the environment provides the resource elsewhere drops the capability and
/// gated tests skip instead of failing at first use.
/// </summary>
[TestFixture]
public sealed class UnconfiguredAddressCapabilityTests
{
    private const string AddressKey = "ProtoTest:Fake:Address";

    [Test]
    public async Task UseBrokerUnlessConfigured_WhenNoKeyIsConfigured_ShouldKeepTheBrokerCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddMessaging(messaging => messaging.UseBrokerUnlessConfigured(_ => new InertBroker(), AddressKey));

        await using var host = builder.Build();

        Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
    }

    [Test]
    public async Task UseBrokerUnlessConfigured_WhenTheKeyIsConfigured_ShouldDropTheBrokerCapabilityAndNameIt()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [AddressKey] = "http://elsewhere.example" }));
        builder.AddMessaging(messaging => messaging.UseBrokerUnlessConfigured(_ => new InertBroker(), AddressKey));
        await using var host = builder.Build();
        await host.StartAsync();

        var skipped = host.Trace.Snapshot().Entries!.Single(entry => entry.Kind == "capability.skipped");
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.False);
            Assert.That(skipped.Attributes["capability.keys"], Does.Contain(AddressKey));
            Assert.That(skipped.Attributes["capability.reason"], Is.EqualTo("already configured"));
        }
    }

    private sealed class InertBroker : IProtoMessageBroker
    {
        public string Name => "Inert";

        public ValueTask PublishAsync(ProtoMessage message, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<IProtoMessageConsumer> CreateConsumerAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The inert broker is never used in these tests.");
    }
}
