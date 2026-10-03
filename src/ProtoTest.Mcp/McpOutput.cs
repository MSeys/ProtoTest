namespace ProtoTest.Mcp;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// The last step of every tool: the JSON an agent reads. Paths under the folder the server reads become
/// relative to it, readable characters stay unescaped, and attributes that repeat an operation's own source
/// location are dropped, so the agent's context goes to the evidence.
/// </summary>
internal static class McpOutput
{
    private static readonly JsonSerializerOptions s_serialize = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // An agent reads this output; it is never embedded in HTML, so only what JSON itself requires is escaped.
    private static readonly JsonSerializerOptions s_write = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly string[] s_locationAttributes = ["code.file.path", "code.line.number", "code.function.name"];

    private const string TypeAttribute = "expected.type";

    /// <summary>Serializes a tool result.</summary>
    public static string Write(object value, string? root)
        => Compact(JsonSerializer.SerializeToNode(value, s_serialize), root);

    /// <summary>Rewrites JSON another component already serialized, such as the diagnosis document.</summary>
    public static string Rewrite(string json, string? root)
        => Compact(JsonNode.Parse(json), root);

    private static string Compact(JsonNode? node, string? root)
    {
        if (node is null)
        {
            return "null";
        }

        var prefix = root is { Length: > 0 }
            ? Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/') + "/"
            : null;
        Visit(node, prefix);
        return node.ToJsonString(s_write);
    }

    private static void Visit(JsonNode node, string? prefix)
    {
        switch (node)
        {
            case JsonObject item:
                if (item["attributes"] is JsonObject attributes)
                {
                    attributes.Remove(TypeAttribute);
                    if (item["sourceFile"] is JsonValue)
                    {
                        foreach (var name in s_locationAttributes)
                        {
                            attributes.Remove(name);
                        }
                    }
                }

                foreach (var (name, value) in item.ToArray())
                {
                    // The root stays absolute: it is what every relative path resolves against.
                    if (name != "root" && Relative(value, prefix) is { } path)
                    {
                        item[name] = path;
                    }
                    else if (value is not null)
                    {
                        Visit(value, prefix);
                    }
                }

                break;
            case JsonArray list:
                for (var index = 0; index < list.Count; index++)
                {
                    if (Relative(list[index], prefix) is { } path)
                    {
                        list[index] = path;
                    }
                    else if (list[index] is { } child)
                    {
                        Visit(child, prefix);
                    }
                }

                break;
        }
    }

    // A path under the root, in either separator style, becomes relative with forward slashes; anything else stays.
    private static string? Relative(JsonNode? value, string? prefix)
    {
        if (prefix is null || value is not JsonValue scalar || !scalar.TryGetValue<string>(out var text))
        {
            return null;
        }

        var normalized = text.Replace('\\', '/');
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return normalized.Length > prefix.Length && normalized.StartsWith(prefix, comparison)
            ? normalized[prefix.Length..]
            : null;
    }
}
