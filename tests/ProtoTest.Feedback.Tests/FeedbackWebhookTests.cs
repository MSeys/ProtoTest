namespace ProtoTest.Feedback.Tests;

using System.Text.Json;
using ProtoTest.TestSupport;

/// <summary>
/// The webhook channel: the digest JSON posted to the configured address with the optional
/// shared-secret header. The post-run webhook is the channel the action's consumer tests.
/// </summary>
[TestFixture]
public sealed class FeedbackWebhookTests
{
    [Test]
    public async Task PostAsync_ShouldPostTheDigestJson()
    {
        await using var server = RecordingHttpServer.Start(200);
        using var client = new HttpClient();
        var digest = FeedbackFixtures.FailedDigest();

        var result = await ProtoFeedbackWebhook.PostAsync(
            digest,
            new ProtoFeedbackTarget { WebhookUrl = new Uri(server.Url) },
            client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Channel, Is.EqualTo(ProtoFeedbackChannels.Webhook));
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Posted));
            Assert.That(result.Reason, Is.Null);

            var request = server.Requests.Single();
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Path, Is.EqualTo("/"));
            Assert.That(request.Headers["Content-Type"], Does.Contain("application/json"));
            Assert.That(request.Body, Is.EqualTo(ProtoFeedback.DigestJson(digest)));
        }

        using var body = JsonDocument.Parse(server.Requests.Single().Body);
        Assert.That(
            body.RootElement.GetProperty("runId").GetString(),
            Is.EqualTo("run-1"));
    }

    [Test]
    public async Task PostAsync_ShouldSendTheSharedSecretHeader()
    {
        await using var server = RecordingHttpServer.Start(200);
        using var client = new HttpClient();

        await ProtoFeedbackWebhook.PostAsync(
            FeedbackFixtures.FailedDigest(),
            new ProtoFeedbackTarget
            {
                WebhookUrl = new Uri(server.Url),
                WebhookSecret = "shared-secret"
            },
            client);

        await ProtoFeedbackWebhook.PostAsync(
            FeedbackFixtures.FailedDigest(),
            new ProtoFeedbackTarget
            {
                WebhookUrl = new Uri(server.Url),
                WebhookSecret = "shared-secret",
                WebhookSecretHeader = "X-Custom-Secret"
            },
            client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(server.Requests[0].Headers[ProtoFeedbackWebhook.DefaultSecretHeader], Is.EqualTo("shared-secret"));
            Assert.That(server.Requests[1].Headers["X-Custom-Secret"], Is.EqualTo("shared-secret"));
        }
    }

    [Test]
    public async Task PostAsync_ShouldPostAGreenRun()
    {
        await using var server = RecordingHttpServer.Start(200);
        using var client = new HttpClient();

        var result = await ProtoFeedbackWebhook.PostAsync(
            FeedbackFixtures.GreenDigest(),
            new ProtoFeedbackTarget { WebhookUrl = new Uri(server.Url) },
            client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Posted));
            Assert.That(server.Requests, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task PostAsync_ShouldSkipWithoutAnAddress()
    {
        using var client = new HttpClient();

        var result = await ProtoFeedbackWebhook.PostAsync(
            FeedbackFixtures.FailedDigest(), new ProtoFeedbackTarget(), client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Skipped));
            Assert.That(result.Reason, Does.Contain("PROTOTEST_FEEDBACK_WEBHOOK_URL"));
        }
    }

    [Test]
    public async Task PostAsync_ShouldFailWhenTheEndpointRefuses()
    {
        await using var server = RecordingHttpServer.Start(500);
        using var client = new HttpClient();

        var result = await ProtoFeedbackWebhook.PostAsync(
            FeedbackFixtures.FailedDigest(),
            new ProtoFeedbackTarget { WebhookUrl = new Uri(server.Url) },
            client);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Failed));
            Assert.That(result.Reason, Does.Contain("500"));
        }
    }

    [Test]
    public async Task PostAsync_ShouldReportAnUnreachableEndpoint()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var port = TestNetworking.FreePort();

        var result = await ProtoFeedbackWebhook.PostAsync(
            FeedbackFixtures.FailedDigest(),
            new ProtoFeedbackTarget { WebhookUrl = new Uri($"http://127.0.0.1:{port}/") },
            client);

        Assert.That(result.Status, Is.EqualTo(ProtoFeedbackStatuses.Failed));
    }
}
