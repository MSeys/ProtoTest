namespace ProtoTest.OpenApi.Internal.Reading;

using System.Text.Json.Nodes;
using ProtoTest.OpenApi.Internal.Model;

/// <summary>
/// Reads schemas into <see cref="OpenApiSpecSchema"/> graphs. Each <c>$ref</c> target is read once and
/// shared, so a component used in many places, or one that refers to itself, is one instance.
/// </summary>
internal sealed class OpenApiSchemaReader(JsonObject root)
{
    private static readonly string[] CompositionKeywords = ["allOf", "oneOf", "anyOf"];
    private readonly Dictionary<string, OpenApiSpecSchema> _byReference = new(StringComparer.Ordinal);

    public OpenApiSpecSchema Read(JsonNode? node)
    {
        if (node is JsonObject json && JsonPointer.Reference(json) is { } reference)
        {
            if (_byReference.TryGetValue(reference, out var shared))
            {
                return shared;
            }

            // Registered before the target is read, so a reference back to it resolves to this instance.
            var schema = new OpenApiSpecSchema();
            _byReference[reference] = schema;
            Fill(schema, JsonPointer.Resolve(root, reference) as JsonObject);
            return schema;
        }

        var inline = new OpenApiSpecSchema();
        Fill(inline, node as JsonObject);
        return inline;
    }

    private void Fill(OpenApiSpecSchema schema, JsonObject? json)
    {
        if (json is null)
        {
            return;
        }

        if (JsonPointer.Reference(json) is not null)
        {
            // A reference that points at another reference: the final target carries the structure.
            schema.Compositions.Add(Read(json));
            return;
        }

        if (json["properties"] is JsonObject properties)
        {
            foreach (var (name, property) in properties)
            {
                schema.Properties[name] = Read(property);
            }
        }

        if (json["items"] is JsonObject items)
        {
            schema.Items = Read(items);
        }

        foreach (var keyword in CompositionKeywords)
        {
            if (json[keyword] is JsonArray members)
            {
                schema.Compositions.AddRange(members.Select(Read));
            }
        }
    }
}
