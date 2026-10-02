namespace ProtoTest.OpenApi.Internal.Reading;

using System.Text.Json.Nodes;

/// <summary>Resolves a local <c>$ref</c> such as <c>#/components/schemas/User</c> against the document root.</summary>
internal static class JsonPointer
{
    /// <summary>The node a local reference points at, or <see langword="null"/> for an external or broken reference.</summary>
    public static JsonNode? Resolve(JsonObject root, string reference)
    {
        if (!reference.StartsWith('#'))
        {
            return null;
        }

        JsonNode? current = root;
        foreach (var token in reference[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Uri.UnescapeDataString(token)
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            current = current switch
            {
                JsonObject json => json[name],
                JsonArray array when int.TryParse(name, out var index) && index >= 0 && index < array.Count => array[index],
                _ => null
            };
            if (current is null)
            {
                return null;
            }
        }

        return current;
    }

    /// <summary>The object a node stands for: itself, or what its chain of <c>$ref</c> points at.</summary>
    public static JsonObject? Follow(JsonObject root, JsonNode? node)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (node is JsonObject json && Reference(json) is { } reference)
        {
            if (!seen.Add(reference))
            {
                return null;
            }

            node = Resolve(root, reference);
        }

        return node as JsonObject;
    }

    /// <summary>The object's <c>$ref</c>, or <see langword="null"/> when it is not a reference.</summary>
    public static string? Reference(JsonObject json)
        => json["$ref"] is JsonValue value && value.TryGetValue<string>(out var reference) ? reference : null;
}
