using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("ProtoTest.OpenApi.Tests")]

namespace ProtoTest.OpenApi.Internal;

using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

internal static class OpenApiSpecLoader
{
    /// <summary>
    /// Loads and parses an OpenAPI document from a local file path, URL, or raw JSON/YAML content.
    /// </summary>
    public static OpenApiDocument Load(string source, string? baseUrl = null, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (File.Exists(source))
        {
            using var stream = File.OpenRead(source);
            var readResult = new OpenApiStreamReader().Read(stream, out var diagnostic);

            ValidateDiagnostics(diagnostic);
            return readResult;
        }

        if (TryResolveHttpUri(source, baseUrl, out var uri))
        {
            return LoadFromUrl(uri, httpClient);
        }

        return LoadFromContent(source);
    }

    /// <summary>
    /// Compatibility alias for loading a local path or raw document content.
    /// </summary>
    public static OpenApiDocument LoadFromPath(string pathOrContent)
        => Load(pathOrContent);

    private static OpenApiDocument LoadFromUrl(Uri uri, HttpClient? httpClient)
    {
        var ownsClient = httpClient is null;
        httpClient ??= new HttpClient();

        try
        {
            var content = httpClient.GetStringAsync(uri).GetAwaiter().GetResult();
            return LoadFromContent(content);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                $"Failed to retrieve OpenAPI specification from '{uri}'.",
                exception);
        }
        finally
        {
            if (ownsClient)
            {
                httpClient.Dispose();
            }
        }
    }

    private static OpenApiDocument LoadFromContent(string content)
    {
        var stringReader = new OpenApiStringReader();
        var result = stringReader.Read(content, out var stringDiagnostic);

        ValidateDiagnostics(stringDiagnostic);
        return result;
    }

    private static bool TryResolveHttpUri(string source, string? baseUrl, out Uri uri)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out uri!) && IsHttpUri(uri))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
            IsHttpUri(baseUri) &&
            Uri.TryCreate(baseUri, source, out uri!) &&
            IsHttpUri(uri))
        {
            return true;
        }

        uri = null!;
        return false;
    }

    private static bool IsHttpUri(Uri uri)
        => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static void ValidateDiagnostics(OpenApiDiagnostic diagnostic)
    {
        if (diagnostic.Errors.Count > 0)
        {
            var errors = string.Join("; ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Failed to parse OpenAPI specification: {errors}");
        }
    }
}