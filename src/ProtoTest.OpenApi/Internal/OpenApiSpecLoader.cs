namespace ProtoTest.OpenApi.Internal;

using ProtoTest.Core;
using ProtoTest.OpenApi.Internal.Model;
using ProtoTest.OpenApi.Internal.Reading;

internal static class OpenApiSpecLoader
{
    /// <summary>
    /// Loads and parses an OpenAPI document from a local file path, URL, or raw JSON/YAML content.
    /// </summary>
    public static OpenApiSpec Load(string source, string? baseUrl = null, HttpClient? httpClient = null)
        => LoadWithContent(source, baseUrl, httpClient).Document;

    /// <summary>Loads an OpenAPI document together with the content it was parsed from, so a collector
    /// can record the document's identity beside its coverage.</summary>
    public static (string Content, OpenApiSpec Document) LoadWithContent(
        string source,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var content = ProtoDocumentSource.LoadText(source, baseUrl, httpClient, "openapi:", "swagger:");
        return (content, Parse(content));
    }

    /// <summary>Parses JSON or YAML document text, throwing with the reason when it is not a readable document.</summary>
    public static OpenApiSpec Parse(string content)
        => OpenApiDocumentReader.Read(OpenApiTextReader.Read(content));
}
