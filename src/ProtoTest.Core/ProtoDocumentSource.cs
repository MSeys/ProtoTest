namespace ProtoTest.Core;

/// <summary>Loads a text document from inline content, a local file, or an HTTP(S) URL.</summary>
public static class ProtoDocumentSource
{
    public static string LoadText(string source, string? baseUrl = null, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (File.Exists(source)) return File.ReadAllText(source);
        if (LooksLikeInlineDocument(source)) return source;
        if (!TryResolveHttpUri(source, baseUrl, out var uri)) return source;

        var ownsClient = httpClient is null;
        httpClient ??= new HttpClient();
        try
        {
            return httpClient.GetStringAsync(uri).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException($"Failed to retrieve document from '{uri}'.", exception);
        }
        finally
        {
            if (ownsClient) httpClient.Dispose();
        }
    }

    private static bool TryResolveHttpUri(string source, string? baseUrl, out Uri uri)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out uri!) && IsHttpUri(uri)) return true;
        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && IsHttpUri(baseUri) &&
            Uri.TryCreate(baseUri, source, out uri!) && IsHttpUri(uri)) return true;

        uri = null!;
        return false;
    }

    private static bool IsHttpUri(Uri uri)
        => uri.Scheme is "http" or "https";

    private static bool LooksLikeInlineDocument(string source)
    {
        var trimmed = source.TrimStart();
        if (trimmed.Contains('\n') || trimmed.StartsWith('{') || trimmed.StartsWith('[')) return true;
        return new[] { "openapi:", "swagger:", "type ", "schema ", "extend ", "directive ", "scalar ", "enum ", "interface ", "union ", "input ", "#" }
            .Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
