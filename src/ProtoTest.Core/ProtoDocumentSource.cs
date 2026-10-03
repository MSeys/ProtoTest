namespace ProtoTest.Core;

/// <summary>Loads a text document from inline content, a local file, or an HTTP(S) URL.</summary>
public static class ProtoDocumentSource
{
    // One client for every retrieval: creating and disposing one per document exhausts sockets across
    // runs that load several documents, and a caller can still pass its own.
    private static readonly HttpClient SharedClient = new();

    /// <summary>
    /// Loads a text document from inline content, a local file, or an HTTP(S) URL.
    /// </summary>
    /// <param name="source">An inline document, a local file path, or an absolute or relative URL.</param>
    /// <param name="baseUrl">The origin a relative URL resolves against, when one is configured.</param>
    /// <param name="httpClient">The client to retrieve a URL with; the shared one is used when omitted.</param>
    /// <param name="inlinePrefixes">
    /// The document-format prefixes the calling integration recognizes - an SDL document starts with
    /// <c>type </c>, an OpenAPI document with <c>openapi:</c>. Core knows only that a multi-line body
    /// or a JSON object/array is inline, so each format's vocabulary stays with that format.
    /// </param>
    public static string LoadText(
        string source,
        string? baseUrl = null,
        HttpClient? httpClient = null,
        params string[] inlinePrefixes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (File.Exists(source)) return File.ReadAllText(source);
        if (LooksLikeInlineDocument(source, inlinePrefixes ?? [])) return source;
        if (!TryResolveHttpUri(source, baseUrl, out var uri)) return source;

        try
        {
            return (httpClient ?? SharedClient).GetStringAsync(uri).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException($"Failed to retrieve document from '{uri}'.", exception);
        }
    }

    /// <summary>
    /// Whether <paramref name="source"/> is a path the application serves (<c>/openapi/v1.json</c>) with
    /// no base address to resolve it against and no file by that name. Such a document loads from the
    /// application once the run can reach it, through <see cref="LoadTextAsync"/>.
    /// </summary>
    public static bool IsApplicationPath(string source, string? baseUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return string.IsNullOrWhiteSpace(baseUrl)
            && source.StartsWith('/')
            && !source.StartsWith("//", StringComparison.Ordinal)
            && !source.Contains('\n')
            && !File.Exists(source);
    }

    /// <summary>Loads a document from an application's client, relative to the client's base address.</summary>
    public static async Task<string> LoadTextAsync(string source, HttpClient client, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(client);
        try
        {
            return await client.GetStringAsync(source, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new InvalidOperationException(
                $"Failed to retrieve document from '{source}' on '{client.BaseAddress}'.", exception);
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

    private static bool LooksLikeInlineDocument(string source, string[] inlinePrefixes)
    {
        var trimmed = source.TrimStart();
        if (trimmed.Contains('\n') || trimmed.StartsWith('{') || trimmed.StartsWith('[')) return true;
        return inlinePrefixes.Any(prefix => trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
