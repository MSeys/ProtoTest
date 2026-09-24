namespace ProtoTest.OpenApi.Internal;

using Microsoft.OpenApi;

internal static class OpenApiSchemaExtractor
{
    public static HashSet<string> ExtractResponseProperties(IOpenApiResponse response)
    {
        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<(IOpenApiSchema Schema, string Path)>();
        if (response.Content is not { } content)
        {
            return properties;
        }

        foreach (var mediaType in content.Values)
        {
            if (mediaType.Schema is not null)
            {
                // The whole response body is a coverage unit: a successful shape match reports "$"
                // itself, so without a baseline row that match could never land.
                properties.Add("$");
                TraverseSchema("$", mediaType.Schema, properties, [], visited);
            }
        }

        return properties;
    }

    private static void TraverseSchema(
        string currentPath,
        IOpenApiSchema schema,
        HashSet<string> result,
        HashSet<IOpenApiSchema> recursionStack,
        HashSet<(IOpenApiSchema Schema, string Path)> visited)
    {
        schema = ResolveReference(schema);
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

            foreach (var composedSchema in (schema.AllOf ?? []).Concat(schema.OneOf ?? []).Concat(schema.AnyOf ?? []))
            {
                TraverseSchema(currentPath, composedSchema, result, recursionStack, visited);
            }

            if (schema.Properties is { Count: > 0 })
            {
                foreach (var (propertyName, propertySchema) in schema.Properties)
                {
                    var childPath = $"{currentPath}.{propertyName}";
                    result.Add(childPath);
                    TraverseSchema(childPath, propertySchema, result, recursionStack, visited);
                }
            }

            if (schema.Items is not null)
            {
                // The array item is a coverage unit of its own, so an indexed match like $.lines[0]
                // (normalized to $.lines[]) has a baseline row to land on.
                var itemPath = $"{currentPath}[]";
                result.Add(itemPath);
                TraverseSchema(itemPath, schema.Items, result, recursionStack, visited);
            }
        }
        finally
        {
            recursionStack.Remove(schema);
        }
    }

    /// <summary>
    /// A schema reached through <c>$ref</c> is an <see cref="OpenApiSchemaReference"/> carrying the
    /// resolved target; traversal follows the target so a shared component is walked once.
    /// </summary>
    private static IOpenApiSchema ResolveReference(IOpenApiSchema schema)
        => schema is OpenApiSchemaReference { Target: { } target } ? target : schema;
}
