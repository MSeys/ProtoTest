namespace ProtoTest.OpenApi.Internal;

using Microsoft.OpenApi.Models;

internal static class OpenApiSchemaExtractor
{
    public static HashSet<string> ExtractResponseProperties(
        OpenApiDocument document,
        OpenApiResponse response)
    {
        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<(OpenApiSchema Schema, string Path)>();

        foreach (var mediaType in response.Content.Values)
        {
            if (mediaType.Schema is not null)
            {
                // The whole response body is a coverage unit: a successful shape match reports "$"
                // itself, so without a baseline row that match could never land.
                properties.Add("$");
                TraverseSchema("$", mediaType.Schema, document, properties, [], visited);
            }
        }

        return properties;
    }

    private static void TraverseSchema(
        string currentPath,
        OpenApiSchema schema,
        OpenApiDocument document,
        HashSet<string> result,
        HashSet<OpenApiSchema> recursionStack,
        HashSet<(OpenApiSchema Schema, string Path)> visited)
    {
        schema = ResolveReference(schema, document);
        if (!recursionStack.Add(schema))
        {
            return;
        }

        try
        {
            // A shared sub-schema reached again at the same path (a diamond) would otherwise repeat
            // its whole traversal; memoizing the schema/path pair keeps the walk linear in the
            // reachable paths.
            if (!visited.Add((schema, currentPath)))
            {
                return;
            }

            foreach (var composedSchema in schema.AllOf.Concat(schema.OneOf).Concat(schema.AnyOf))
            {
                TraverseSchema(currentPath, composedSchema, document, result, recursionStack, visited);
            }

            if (schema.Properties is { Count: > 0 })
            {
                foreach (var (propertyName, propertySchema) in schema.Properties)
                {
                    var childPath = $"{currentPath}.{propertyName}";
                    result.Add(childPath);
                    TraverseSchema(childPath, propertySchema, document, result, recursionStack, visited);
                }
            }

            if (schema.Items is not null)
            {
                // The array item is a coverage unit of its own, so an indexed match like $.lines[0]
                // (normalized to $.lines[]) has a baseline row to land on.
                var itemPath = $"{currentPath}[]";
                result.Add(itemPath);
                TraverseSchema(itemPath, schema.Items, document, result, recursionStack, visited);
            }
        }
        finally
        {
            recursionStack.Remove(schema);
        }
    }

    private static OpenApiSchema ResolveReference(OpenApiSchema schema, OpenApiDocument document)
    {
        var referenceId = schema.Reference?.Id;
        if (!string.IsNullOrWhiteSpace(referenceId)
            && document.Components?.Schemas.TryGetValue(referenceId, out var referencedSchema) == true)
        {
            return referencedSchema;
        }

        return schema;
    }
}
