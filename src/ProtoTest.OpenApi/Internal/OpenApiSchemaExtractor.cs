namespace ProtoTest.OpenApi.Internal;

using ProtoTest.OpenApi.Internal.Model;

internal static class OpenApiSchemaExtractor
{
    public static HashSet<string> ExtractResponseProperties(OpenApiSpecResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<(OpenApiSpecSchema Schema, string Path)>();
        foreach (var schema in response.BodySchemas)
        {
            // The whole response body is a coverage unit: a successful shape match reports "$"
            // itself, so without a baseline row that match could never land.
            properties.Add("$");
            TraverseSchema("$", schema, properties, [], visited);
        }

        return properties;
    }

    private static void TraverseSchema(
        string currentPath,
        OpenApiSpecSchema schema,
        HashSet<string> result,
        HashSet<OpenApiSpecSchema> recursionStack,
        HashSet<(OpenApiSpecSchema Schema, string Path)> visited)
    {
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

            foreach (var composedSchema in schema.Compositions)
            {
                TraverseSchema(currentPath, composedSchema, result, recursionStack, visited);
            }

            foreach (var (propertyName, propertySchema) in schema.Properties)
            {
                var childPath = $"{currentPath}.{propertyName}";
                result.Add(childPath);
                TraverseSchema(childPath, propertySchema, result, recursionStack, visited);
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
}
