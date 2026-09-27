namespace ProtoTest.Extensibility.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Messaging;
using ProtoTest.Web;

/// <summary>
/// Proves the documented extension points are reachable with the public surface alone. This project is
/// deliberately absent from every InternalsVisibleTo grant: it compiles against the packages exactly
/// like a third-party adapter package would, so a regression that makes an extension point friend-only
/// fails this build instead of a consumer repository.
/// </summary>
[TestFixture]
public sealed class PublicSeamTests
{
    [Test]
    public async Task MinimalBroker_ShouldPublishAndAwaitThroughThePublicSeam()
    {
        var broker = new MinimalBroker();
        var builder = new ProtoHostBuilder();
        builder.AddMessaging(messaging => messaging.UseBroker(_ => broker));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("extensibility broker", TestMethods.Placeholder);

        await context.Messaging().PublishAsync("orders", "{\"id\":7}", contentType: "application/json");
        var received = await context.Messaging().AwaitAsync(
            "orders",
            message => message.Payload == "{\"id\":7}",
            TimeSpan.FromSeconds(2));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Broker), Is.True,
                "UseBroker declares the broker capability for the adapter that won registration");
            Assert.That(broker.PublishedCount, Is.EqualTo(1));
            Assert.That(received.Destination, Is.EqualTo("orders"));
            Assert.That(received.Payload, Is.EqualTo("{\"id\":7}"));
        }
    }

    [Test]
    public async Task MinimalWebBackend_ShouldComposeThePublicWebBuildingBlocks()
    {
        var builder = new ProtoHostBuilder();
        builder.AddWebBackend(new MinimalWebBackendFactory());
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Web:Minimal:ActionTimeout"] = "00:00:09"
            }));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("extensibility web", TestMethods.Placeholder);

        var session = context.Web();
        var backend = await session.GetBackendAsync<MinimalWebBackend>();
        await backend.NavigateAsync(new Uri("https://orders.example.test/"));
        await backend.ClickAsync(new WebElementReference([], "Orders", "Approve", By.TestId("approve")));
        await session.WaitUntilAsync(
            _ => ValueTask.FromResult(true),
            TimeSpan.FromSeconds(2),
            "the session's own polling loop");
        var attachments = await backend.CaptureFailureAsync(
            new WebFailureContext("click", null, new InvalidOperationException("minimal failure")));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.ActionTimeout, Is.EqualTo(TimeSpan.FromSeconds(9)),
                "the options facade bound the backend section over the code default");
            Assert.That(backend.PollInterval, Is.EqualTo(WebBackendDefaults.DefaultPollInterval),
                "the timing facade supplies the poll default the session's assertions read");
            Assert.That(backend.Address, Is.EqualTo("https://orders.example.test/"));
            Assert.That(backend.ClickAttempts, Is.GreaterThanOrEqualTo(2),
                "the public probe loop retried the transient actionability failure");
            Assert.That(attachments.Select(attachment => attachment.Name), Is.EqualTo(new[]
            {
                "web-default-click-1-failure.png",
                "web-default-click-1-page.html",
                "web-default-click-1-location.txt"
            }), "the failure-artifact facade keeps the documented naming rule");
            Assert.That(WebMediaTypes.Guess("report.csv"), Is.EqualTo("text/csv"));
            Assert.That(WebMediaTypes.Guess("report.unknown"), Is.EqualTo(WebMediaTypes.Default));
            Assert.That(WebArtifactNames.SafeName("Orders/Approve"), Is.EqualTo("orders-approve"));
        }
    }
}
