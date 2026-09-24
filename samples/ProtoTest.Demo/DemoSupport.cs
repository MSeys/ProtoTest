namespace ProtoTest.Demo;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;

/// <summary>Small helpers shared by the journeys so each test reads as the story it tells.</summary>
internal sealed class DemoSupport(ProtoExecutionContext context)
{
    public RestRequestBuilder As(string token)
        => context.Rest().WithoutAuth().Header("Authorization", $"Bearer {token}");

    /// <summary>Creates the in-process webhook sink the delivery tests steer through test support.</summary>
    public ValueTask<WebhookSinkResponse> CreateSinkAsync(int failuresBeforeSuccess)
        => context.Data()
            .For<ConfigureWebhookSinkRequest>()
            .With(request => request.FailuresBeforeSuccess, failuresBeforeSuccess)
            .CreateAsync<WebhookSinkResponse>();

    public async Task<IReadOnlyList<WebhookReceiptResponse>> ReceiptsAsync(string sinkId)
    {
        using var response = await context.Rest()
            .WithoutAuth()
            .GetAsync("/test-support/webhook-sinks/{sinkId}/receipts", new { sinkId });
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<IReadOnlyList<WebhookReceiptResponse>>()!;
    }

    /// <summary>
    /// Polls the delivered webhooks until the event arrives, through the same bounded polling engine
    /// every other wait uses, and reports the timeout the way the journeys expect.
    /// </summary>
    public async Task<WebhookDeliveryResponse> WaitForDeliveredAsync(string eventType)
    {
        var result = await ProtoPolling.PollAsync(
            async cancellationToken =>
            {
                using var response = await context.Rest()
                    .GetAsync(
                        "/api/v1/webhook-deliveries",
                        new { status = WebhookDeliveryStatuses.Delivered },
                        ct: cancellationToken);
                var page = response.ReadAsJson<CursorPage<WebhookDeliveryResponse>>()!;
                return page.Items.FirstOrDefault(delivery => delivery.EventType == eventType);
            },
            match => match is not null,
            TimeSpan.FromSeconds(5),
            ProtoPolling.DefaultInterval,
            default);
        return result.Value
            ?? throw new TimeoutException($"No delivered '{eventType}' webhook arrived within the timeout.");
    }

    public string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return $"sha256={Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()}";
    }
}

internal static class DemoContextExtensions
{
    /// <summary>The demo helpers bound to this test, so a helper never reads the ambient context.</summary>
    public static DemoSupport Demo(this ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new DemoSupport(context);
    }
}
