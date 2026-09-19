namespace ProtoTest.Demo;

using System.Net;
using System.Security.Cryptography;
using System.Text;
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Rest;
using ProtoTest.SampleApp.Contracts;

/// <summary>Small helpers shared by the journeys so each test reads as the story it tells.</summary>
internal static class DemoSupport
{
    public static RestRequestBuilder As(string token)
        => Proto.Context.Rest().WithoutAuth().Header("Authorization", $"Bearer {token}");

    /// <summary>Creates the in-process webhook sink the delivery tests steer through test support.</summary>
    public static ValueTask<WebhookSinkResponse> CreateSinkAsync(int failuresBeforeSuccess)
        => Proto.Context.Data()
            .For<ConfigureWebhookSinkRequest>()
            .With(request => request.FailuresBeforeSuccess, failuresBeforeSuccess)
            .CreateAsync<WebhookSinkResponse>();

    public static async Task<IReadOnlyList<WebhookReceiptResponse>> ReceiptsAsync(string sinkId)
    {
        using var response = await Proto.Context.Rest()
            .WithoutAuth()
            .GetAsync("/test-support/webhook-sinks/{sinkId}/receipts", new { sinkId });
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
        return response.ReadAsJson<IReadOnlyList<WebhookReceiptResponse>>()!;
    }

    public static async Task<WebhookDeliveryResponse> WaitForDeliveredAsync(string eventType)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await Proto.Context.Rest()
                .GetAsync("/api/v1/webhook-deliveries", new { status = WebhookDeliveryStatuses.Delivered });
            var page = response.ReadAsJson<CursorPage<WebhookDeliveryResponse>>()!;
            var match = page.Items.FirstOrDefault(delivery => delivery.EventType == eventType);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"No delivered '{eventType}' webhook arrived within the timeout.");
    }

    public static string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return $"sha256={Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()}";
    }
}
