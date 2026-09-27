namespace ProtoTest.Messaging.MassTransit.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Messaging.MassTransit.TestApi;
using ProtoTest.TestSupport;
using SampleApi = ProtoTest.AspNetCore.SampleApi;

/// <summary>
/// Pins the Broker capability's in-process rule: the MassTransit harness exists only while the
/// application is hosted in-process, so the capability is declared only while the application's
/// <c>BaseUrl</c> is not configured - a published application drops it and gated tests skip instead of
/// failing at setup or first use. The failure paths name the fix when the composition cannot serve.
/// </summary>
[TestFixture]
public sealed class MassTransitCapabilityTests
{
    private const string BaseUrlKey = "ProtoTest:Applications:Default:BaseUrl";

    [Test]
    public async Task UseMassTransit_WhenTheApplicationIsHostedInProcess_ShouldKeepTheBrokerCapability()
    {
        var builder = MassTransitSuite.Builder();

        await using var host = builder.Build();

        Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True);
    }

    [Test]
    public async Task UseMassTransit_WhenTheApplicationAddressIsConfigured_ShouldDropTheBrokerCapabilityAndNameTheKey()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [BaseUrlKey] = "http://published.example" }));
        builder.AddApplication("Default", app => app.AddAspNetCoreServer<Program>());
        builder.AddMessaging(messaging => messaging.UseMassTransit<Program>());
        await using var host = builder.Build();
        await host.StartAsync();

        // The server capability drops with the same key; the broker entry is the one this test pins.
        var skipped = host.Trace.Snapshot().Entries!.Single(entry =>
            entry.Kind == "capability.skipped"
            && entry.Attributes["capability.kind"] == ProtoCapabilityKinds.Broker);
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.False);
            Assert.That(skipped.Attributes["capability.keys"], Does.Contain(BaseUrlKey));
            Assert.That(skipped.Attributes["capability.reason"], Is.EqualTo("already configured"));
        }
    }

    [Test]
    public async Task UseMassTransit_WhenNoInProcessServerIsRegistered_ShouldFailNamingAddAspNetCoreServer()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddMessaging(messaging => messaging.UseMassTransit<Program>());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit no server", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().PublishAsync(nameof(InvoicePaid), """{"invoiceId":1,"amount":1}"""));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("AddAspNetCoreServer"));
            Assert.That(error.Message, Does.Contain("AddMessaging after it"));
        }
    }

    [Test]
    public async Task UseMassTransit_WhenTheApplicationHasNoTestHarness_ShouldFailNamingAddMassTransitTestHarness()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddAspNetCoreServer<SampleApi.Program>("Default");
        builder.AddMessaging(messaging => messaging.Tap("PaymentReceived").UseMassTransit<SampleApi.Program>());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("masstransit no harness", TestMethods.Placeholder);

        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await context.Messaging().AwaitAsync("PaymentReceived", _ => true, TimeSpan.FromMilliseconds(100)));

        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Does.Contain("AddMassTransitTestHarness"));
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Broker),
                Is.True,
                "the composition promises the bridge; the missing harness is a setup failure, not a capability lie");
        }
    }
}
