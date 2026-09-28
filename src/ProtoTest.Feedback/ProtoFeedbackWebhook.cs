namespace ProtoTest.Feedback;

using System.Net.Http.Headers;
using System.Text;
using ProtoTest.Diagnosis;

/// <summary>
/// The webhook channel: the digest JSON posted to the configured address, with an optional
/// shared-secret header. It posts every digest, green runs included, because a machine consumer
/// decides what to do with it; only a missing address skips.
/// </summary>
public static class ProtoFeedbackWebhook
{
    /// <summary>The header name a shared secret rides when the target names none.</summary>
    public const string DefaultSecretHeader = "X-ProtoTest-Secret";

    /// <summary>Posts the digest JSON. A missing URL skips with the reason; a refused post fails.</summary>
    public static async Task<ProtoFeedbackChannelResult> PostAsync(
        ProtoDiagnosisDocument digest,
        ProtoFeedbackTarget target,
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(client);

        if (target.WebhookUrl is null)
        {
            return new ProtoFeedbackChannelResult(
                ProtoFeedbackChannels.Webhook,
                ProtoFeedbackStatuses.Skipped,
                "No webhook URL: set PROTOTEST_FEEDBACK_WEBHOOK_URL.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, target.WebhookUrl)
        {
            Content = new StringContent(ProtoFeedback.DigestJson(digest), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrEmpty(target.WebhookSecret))
        {
            var header = string.IsNullOrWhiteSpace(target.WebhookSecretHeader)
                ? DefaultSecretHeader
                : target.WebhookSecretHeader;
            request.Headers.TryAddWithoutValidation(header, target.WebhookSecret);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? new ProtoFeedbackChannelResult(ProtoFeedbackChannels.Webhook, ProtoFeedbackStatuses.Posted, null)
                : new ProtoFeedbackChannelResult(
                    ProtoFeedbackChannels.Webhook,
                    ProtoFeedbackStatuses.Failed,
                    $"The webhook answered {Status(response)}.");
        }
        catch (HttpRequestException exception)
        {
            return new ProtoFeedbackChannelResult(
                ProtoFeedbackChannels.Webhook,
                ProtoFeedbackStatuses.Failed,
                $"The webhook post failed: {exception.Message}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProtoFeedbackChannelResult(
                ProtoFeedbackChannels.Webhook, ProtoFeedbackStatuses.Failed, "The webhook post timed out.");
        }
    }

    private static string Status(HttpResponseMessage response)
        => string.IsNullOrEmpty(response.ReasonPhrase)
            ? ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{(int)response.StatusCode} {response.ReasonPhrase}";
}
