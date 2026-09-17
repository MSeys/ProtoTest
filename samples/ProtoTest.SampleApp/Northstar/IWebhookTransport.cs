namespace ProtoTest.SampleApp.Northstar;

using System.Text;

internal sealed record WebhookDispatch(
    string Url,
    string EventType,
    string DeliveryId,
    string Signature,
    string Payload);

/// <summary>
/// Delivers a signed webhook. Production uses <see cref="HttpWebhookTransport"/>; the sample suite
/// swaps in an in-process transport so deliveries stay deterministic without a network socket.
/// </summary>
internal interface IWebhookTransport
{
    Task<(bool Success, string? Error)> SendAsync(WebhookDispatch dispatch, CancellationToken cancellationToken);
}

internal sealed class HttpWebhookTransport(IHttpClientFactory clients) : IWebhookTransport
{
    public async Task<(bool Success, string? Error)> SendAsync(WebhookDispatch dispatch, CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(nameof(HttpWebhookTransport));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, dispatch.Url)
            {
                Content = new StringContent(dispatch.Payload, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("X-Northstar-Event", dispatch.EventType);
            request.Headers.TryAddWithoutValidation("X-Northstar-Delivery", dispatch.DeliveryId);
            request.Headers.TryAddWithoutValidation("X-Northstar-Signature", dispatch.Signature);
            using var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, null)
                : (false, $"http_{(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return (false, exception.GetType().Name);
        }
    }
}
