namespace ProtoTest.OpenApi.Internal;

using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using ProtoTest.Core;

internal static class OpenApiSpecLoader
{
    // JSON is built in; YAML is the separate reader package, registered once for every document.
    private static readonly OpenApiReaderSettings ReaderSettings = CreateReaderSettings();

    /// <summary>
    /// Loads and parses an OpenAPI document from a local file path, URL, or raw JSON/YAML content.
    /// </summary>
    public static OpenApiDocument Load(string source, string? baseUrl = null, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return LoadFromContent(ProtoDocumentSource.LoadText(source, baseUrl, httpClient, "openapi:", "swagger:"));
    }

    private static OpenApiDocument LoadFromContent(string content)
    {
        var result = OpenApiDocument.Parse(content, settings: ReaderSettings);
        ValidateDiagnostics(result.Diagnostic);
        return result.Document
            ?? throw new InvalidOperationException(
                "Failed to parse OpenAPI specification: the reader produced no document.");
    }

    private static OpenApiReaderSettings CreateReaderSettings()
    {
        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();
        return settings;
    }

    private static void ValidateDiagnostics(OpenApiDiagnostic? diagnostic)
    {
        if (diagnostic is { Errors.Count: > 0 })
        {
            var errors = string.Join("; ", diagnostic.Errors.Select(error => error.Message));
            throw new InvalidOperationException($"Failed to parse OpenAPI specification: {errors}");
        }
    }
}
