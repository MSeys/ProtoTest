namespace ProtoTest.Http;

/// <summary>
/// The send-and-read mechanics every HTTP-based protocol shares: send with response headers read, cap
/// the buffered body through <see cref="ProtoHttpResponseBuffer"/>, then read it as text and bytes. A
/// failure while reading disposes the response, so a caller cannot leak one by forgetting a finally
/// block; on success the caller owns <see cref="ProtoHttpExchangeResult.Response"/>.
/// </summary>
public static class ProtoHttpExchange
{
    /// <summary>Sends a request and reads its complete body under the protocol's byte cap.</summary>
    public static async ValueTask<ProtoHttpExchangeResult> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        int maxResponseBodyBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(maxResponseBodyBytes);

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        try
        {
            var bodyBytes = await ProtoHttpResponseBuffer.BufferAsync(response, maxResponseBodyBytes, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ProtoHttpExchangeResult(response, body, bodyBytes);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }
}

/// <summary>One completed exchange: the response the caller now owns, its text body and buffered bytes.</summary>
public sealed record ProtoHttpExchangeResult(HttpResponseMessage Response, string Body, byte[] BodyBytes);
