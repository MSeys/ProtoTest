namespace ProtoTest.OpenApi.Internal;

using Microsoft.OpenApi.Models;

internal static class OpenApiSchemaExtractor
{
    public static HashSet<string> ExtractResponseProperties(
        OpenApiDocument document,
        OpenApiResponse response)
    {
        var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mediaType in response.Content.Values)
        {
            if (mediaType.Schema is not null)
            {
                TraverseSchema("$", mediaType.Schema, document, properties, []);
            }
        }

        return properties;
    }

    private static void TraverseSchema(
        string currentPath,
        OpenApiSchema schema,
        OpenApiDocument document,
        HashSet<string> result,
        HashSet<OpenApiSchema> recursionStack)
    {
        schema = ResolveReference(schema, document);
        if (!recursionStack.Add(schema))
        {
            return;
        }

        foreach (var composedSchema in schema.AllOf.Concat(schema.OneOf).Concat(schema.AnyOf))
        {
            TraverseSchema(currentPath, composedSchema, document, result, recursionStack);
        }

        if (schema.Properties is { Count: > 0 })
        {
            foreach (var (propertyName, propertySchema) in schema.Properties)
            {
                var childPath = $"{currentPath}.{propertyName}";
                result.Add(childPath);
                TraverseSchema(childPath, propertySchema, document, result, recursionStack);
            }
        }

        if (schema.Items is not null)
        {
            TraverseSchema($"{currentPath}[]", schema.Items, document, result, recursionStack);
        }

        recursionStack.Remove(schema);
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
