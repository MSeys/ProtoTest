namespace ProtoTest.Http;

/// <summary>Shared header application for HTTP-based integrations.</summary>
public static class ProtoHttpHeaders
{
    /// <summary>
    /// Adds every configured header to the request. A header that can be added to neither the request
    /// nor its content is an error rather than a silent drop; the content fallback exists because some
    /// headers (for example a custom <c>Content-Type</c>) only apply to the body.
    /// </summary>
    public static void Apply(HttpRequestMessage request, IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var (name, value) in headers)
        {
            if (!request.Headers.TryAddWithoutValidation(name, value)
                && (request.Content is null || !request.Content.Headers.TryAddWithoutValidation(name, value)))
            {
                throw new InvalidOperationException($"Header '{name}' could not be added to the request.");
            }
        }
    }
}
