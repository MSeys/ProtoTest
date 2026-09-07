namespace ProtoTest.OpenApi.Internal;

using Microsoft.OpenApi.Models;

public static class OpenApiSchemaExtractor
{
    /// <summary>
    /// Extracts all expected JSON property paths from response schemas matching successful status codes (200, 201, 202, 204).
    /// </summary>
    public static HashSet<string> ExtractResponseProperties(OpenApiOperation operation)
    {
        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var successResponses = operation.Responses
            .Where(r => r.Key.StartsWith("2"))
            .Select(r => r.Value);

        foreach (var response in successResponses)
        {
            if (response?.Content == null) continue;

            foreach (var mediaType in response.Content.Values)
            {
                if (mediaType.Schema != null)
                {
                    TraverseSchema("$", mediaType.Schema, properties);
                }
            }
        }

        return properties;
    }

    private static void TraverseSchema(string currentPath, OpenApiSchema schema, HashSet<string> result)
    {
        if (schema.Type == "object" && schema.Properties != null)
        {
            foreach (var (propName, propSchema) in schema.Properties)
            {
                var childPath = $"{currentPath}.{propName}";
                result.Add(childPath);
                TraverseSchema(childPath, propSchema, result);
            }
        }
        else if (schema.Type == "array" && schema.Items != null)
        {
            TraverseSchema(currentPath, schema.Items, result);
        }
    }
}