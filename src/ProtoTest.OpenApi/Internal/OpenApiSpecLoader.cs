namespace ProtoTest.OpenApi.Internal;

using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using ProtoTest.Core;

internal static class OpenApiSpecLoader
{
    /// <summary>
    /// Loads and parses an OpenAPI document from a local file path, URL, or raw JSON/YAML content.
    /// </summary>
    public static OpenApiDocument Load(string source, string? baseUrl = null, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return LoadFromContent(ProtoDocumentSource.LoadText(source, baseUrl, httpClient));
    }

    private static OpenApiDocument LoadFromContent(string content)
    {
        var stringReader = new OpenApiStringReader();
        var result = stringReader.Read(content, out var stringDiagnostic);

        ValidateDiagnostics(stringDiagnostic);
        return result;
    }

    private static void ValidateDiagnostics(OpenApiDiagnostic diagnostic)
    {
        if (diagnostic.Errors.Count > 0)
        {
            var errors = string.Join("; ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"Failed to parse OpenAPI specification: {errors}");
        }
    }
}
