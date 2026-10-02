namespace ProtoTest.OpenApi.Internal.Reading;

using System.Text.Json.Nodes;
using ProtoTest.OpenApi.Internal.Model;

/// <summary>
/// Reads the paths, operations and responses of an OpenAPI 3 or Swagger 2.0 document. A 3.x response
/// carries a schema per media type under <c>content</c>; a 2.0 response carries one <c>schema</c>.
/// </summary>
internal static class OpenApiDocumentReader
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];
    private static readonly string[] VersionKeys = ["openapi", "swagger"];

    public static OpenApiSpec Read(JsonObject root)
    {
        var version = Version(root)
            ?? throw OpenApiReadException.Create("the document has no 'openapi' or 'swagger' version.");
        if (root["info"] is not JsonObject)
        {
            throw OpenApiReadException.Create("the document has no 'info' object.");
        }

        // OpenAPI 3.1 allows a document with only webhooks or components; earlier versions require paths.
        if (root["paths"] is not JsonObject paths)
        {
            if (!version.StartsWith("3.1", StringComparison.Ordinal))
            {
                throw OpenApiReadException.Create("the document has no 'paths' object.");
            }

            paths = [];
        }

        var schemas = new OpenApiSchemaReader(root);
        var result = new Dictionary<string, OpenApiSpecPath>(StringComparer.Ordinal);
        foreach (var (template, node) in paths)
        {
            if (JsonPointer.Follow(root, node) is { } pathItem)
            {
                result[template] = ReadPath(root, pathItem, schemas);
            }
        }

        return new OpenApiSpec(result);
    }

    private static string? Version(JsonObject root)
    {
        foreach (var key in VersionKeys)
        {
            if (root[key] is JsonValue value && value.ToString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private static OpenApiSpecPath ReadPath(JsonObject root, JsonObject pathItem, OpenApiSchemaReader schemas)
    {
        var operations = new Dictionary<string, OpenApiSpecOperation>(StringComparer.Ordinal);
        foreach (var (name, node) in pathItem)
        {
            if (Methods.Contains(name, StringComparer.OrdinalIgnoreCase) && node is JsonObject operation)
            {
                operations[name.ToUpperInvariant()] = ReadOperation(root, operation, schemas);
            }
        }

        return new OpenApiSpecPath(operations);
    }

    private static OpenApiSpecOperation ReadOperation(JsonObject root, JsonObject operation, OpenApiSchemaReader schemas)
    {
        var responses = new Dictionary<string, OpenApiSpecResponse>(StringComparer.Ordinal);
        if (operation["responses"] is JsonObject documented)
        {
            foreach (var (key, node) in documented)
            {
                responses[key] = ReadResponse(JsonPointer.Follow(root, node), schemas);
            }
        }

        return new OpenApiSpecOperation(responses);
    }

    private static OpenApiSpecResponse ReadResponse(JsonObject? response, OpenApiSchemaReader schemas)
    {
        var bodies = new List<OpenApiSpecSchema>();
        if (response?["content"] is JsonObject content)
        {
            foreach (var (_, mediaType) in content)
            {
                if (mediaType is JsonObject media && media["schema"] is JsonObject schema)
                {
                    bodies.Add(schemas.Read(schema));
                }
            }
        }
        else if (response?["schema"] is JsonObject schema)
        {
            bodies.Add(schemas.Read(schema));
        }

        return new OpenApiSpecResponse(bodies);
    }
}
