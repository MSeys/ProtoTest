namespace Northstar.ProtoTest;

using System.Net;
using global::NUnit.Framework;
using global::ProtoTest.Core;
using global::ProtoTest.Data;
using global::ProtoTest.Http;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

/// <summary>
/// The lagging-read journey: a background dispatcher delivers webhooks on its own timer, so the read
/// that reports a delivery can trail the write that caused it. The test polls that read with a
/// deadline instead of sleeping, and stops as soon as the delivery shows up. The drill reads once
/// instead, so it passes or fails depending on whether the dispatcher ran first.
/// </summary>
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class WebhookJourney
{
    /// <summary>Timing, wrong: one read right after the write races the dispatcher's 100 ms timer.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task OneReadRacesTheDispatcher()
    {
        FailureDrills.RequireDrills();
        await SubscribeAndCreateAProjectAsync();

        using var deliveries = await Proto.Context.Rest().GetAsync("/api/v1/webhook-deliveries");
        deliveries
            .Should.HaveHttpStatus(HttpStatusCode.OK)
            .Should.MatchShape(new
            {
                items = new[]
                {
                    new { eventType = WebhookEventTypes.ProjectCreated, status = WebhookDeliveryStatuses.Delivered },
                },
            });
    }

    /// <summary>Timing, fixed: polling the same read with a deadline waits as long as the dispatcher needs.</summary>
    [ProtoTest]
    [SignedInAs]
    public async Task CreatingAProjectDeliversItsWebhook()
    {
        await SubscribeAndCreateAProjectAsync();

        // The dispatcher runs every 100 ms, so the first read can still miss the delivery.
        var delivered = await ProtoPolling.PollAsync(
            async cancellationToken =>
            {
                using var page = await Proto.Context.Rest().GetAsync(
                    "/api/v1/webhook-deliveries",
                    new { status = WebhookDeliveryStatuses.Delivered },
                    cancellationToken);
                return page
                    .Should.HaveHttpStatus(HttpStatusCode.OK)
                    .ReadRequired<CursorPage<WebhookDeliveryResponse>>()
                    .Items;
            },
            deliveries => deliveries.Count > 0,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(100),
            Proto.Context.CancellationToken);

        Assert.That(delivered.Satisfied, Is.True, $"no delivery after {delivered.Elapsed.TotalMilliseconds:F0} ms");
        Assert.That(delivered.Value.Single().EventType, Is.EqualTo(WebhookEventTypes.ProjectCreated));
    }

    /// <summary>Subscribes a webhook sink to <c>project.created</c>, then creates one project.</summary>
    private static async Task SubscribeAndCreateAProjectAsync()
    {
        var sink = await Proto.Context.Data().For<ConfigureWebhookSinkRequest>().CreateAsync<WebhookSinkResponse>();
        using var webhook = await Proto.Context.Rest()
            .Body(new CreateWebhookRequest(sink.Url.ToString(), [WebhookEventTypes.ProjectCreated]))
            .PostAsync("/api/v1/webhooks");
        webhook.Should.HaveHttpStatus(HttpStatusCode.Created);

        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest($"hooked-{Proto.Context.TestId}"))
            .PostAsync("/api/v1/projects");
        created.Should.HaveHttpStatus(HttpStatusCode.Created);
    }
}
