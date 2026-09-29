namespace ProtoTest.Messaging.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Pins the in-process address direction: <c>UseBrokerWhenInProcess</c> declares the Broker capability
/// while the named application is served in-process, drops it when the winning provider is a different
/// process boundary, and keeps the configured-keys rule when the host declares no chain.
/// </summary>
[TestFixture]
public sealed class InProcessAddressCapabilityTests
{
    private const string AddressKey = "ProtoTest:Fake:Address";

    private static ProtoCapabilityDescriptor ServerCapability { get; } = new(
        "ASP.NET Core", ProtoCapabilityKinds.Server, "Tests");

    [Test]
    public async Task UseBrokerWhenInProcess_WhenTheChainServesTheApplicationInProcess_ShouldKeepTheBrokerCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app.Providers.Use(new ProtoTargetProvider("in-process")
        {
            Capabilities = [ServerCapability]
        }));
        builder.AddMessaging(messaging => messaging.UseBrokerWhenInProcess(_ => new InertBroker(), "Api", AddressKey));

        await using var host = builder.Build();

        Assert.That(
            host.HasCapability(ProtoCapabilityKinds.Broker),
            Is.True,
            "the chain's winner serves the application in-process, so the adapter exists");
    }

    [Test]
    public async Task UseBrokerWhenInProcess_WhenTheChainServesTheApplicationExternally_ShouldDropTheCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddApplication("Api", app => app.Providers.Use(new ProtoTargetProvider("external")));
        builder.AddMessaging(messaging => messaging.UseBrokerWhenInProcess(_ => new InertBroker(), "Api", AddressKey));
        await using var host = builder.Build();
        await host.StartAsync();

        var skipped = host.Trace.Snapshot().Entries!.Single(entry =>
            entry.Kind == "capability.skipped"
            && entry.Attributes["capability.kind"] == ProtoCapabilityKinds.Broker);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.False);
            Assert.That(skipped.Attributes["capability.reason"], Does.Contain("'external'"));
            Assert.That(skipped.Attributes["capability.reason"], Does.Contain("does not run it in-process"));
        }
    }

    [Test]
    public async Task UseBrokerWhenInProcess_WithoutAChain_WhenTheKeyIsConfigured_ShouldDropTheCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [AddressKey] = "http://elsewhere.example" }));
        builder.AddMessaging(messaging => messaging.UseBrokerWhenInProcess(_ => new InertBroker(), "Api", AddressKey));
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

    [Test]
    public async Task UseBrokerWhenInProcess_WithoutAChain_WhenNoKeyIsConfigured_ShouldKeepTheBrokerCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddMessaging(messaging => messaging.UseBrokerWhenInProcess(_ => new InertBroker(), "Api", AddressKey));

        await using var host = builder.Build();

        Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
    }

    [Test]
    public void UseBrokerWhenInProcess_WithoutAnApplication_ShouldThrow()
    {
        var builder = new ProtoHostBuilder();

        Assert.That(
            () => builder.AddMessaging(messaging => messaging.UseBrokerWhenInProcess(_ => new InertBroker(), " ")),
            Throws.ArgumentException);
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
