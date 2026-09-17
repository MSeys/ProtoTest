namespace ProtoTest.SampleApp.Northstar;

using System.Security.Cryptography;
using System.Text;

/// <summary>Drains the webhook outbox with retries and HMAC-SHA256 request signing.</summary>
internal sealed class WebhookDispatcher(
    NorthstarStore store,
    IWebhookTransport transport,
    ILogger<WebhookDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(100);
    private const int BatchSize = 25;

    public static string Sign(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "A webhook dispatch iteration failed.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        foreach (var job in store.TakePendingDeliveries(BatchSize))
        {
            var dispatch = new WebhookDispatch(
                job.EndpointUrl,
                job.EventType,
                job.DeliveryId,
                Sign(job.Secret, job.Payload),
                job.Payload);
            var (success, error) = await transport.SendAsync(dispatch, cancellationToken);
            store.ReportDelivery(job.OrganizationSlug, job.DeliveryId, success, error);
        }
    }
}
