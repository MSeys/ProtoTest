namespace ProtoTest.Json;

using System.Text.Json;
using System.Text.Json.Nodes;

public class JsonDiagnosticOptions
{
    public bool RedactSensitiveData { get; set; } = true;
    public int MaxDiagnosticBodyLength { get; set; } = 64 * 1024;
    public List<string> SensitiveJsonProperties { get; set; } =
        ["password", "token", "access_token", "refresh_token", "secret", "apiKey", "api_key"];
}

public static class JsonDiagnosticSanitizer
{
    public static string Serialize(object? value, JsonDiagnosticOptions? configured = null)
    {
        try
        {
            return Sanitize(JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }), configured);
        }
        catch (Exception exception)
        {
            return JsonSerializer.Serialize(new
            {
                unavailable = true,
                type = value?.GetType().FullName,
                reason = exception.GetType().Name
            });
        }
    }

    public static string Sanitize(string content, JsonDiagnosticOptions? configured = null, bool truncate = true)
    {
        var options = configured ?? new JsonDiagnosticOptions();
        var result = content ?? string.Empty;
        if (options.RedactSensitiveData && LooksLikeJson(result))
        {
            try
            {
                var node = JsonNode.Parse(result);
                if (node is not null)
                {
                    if (Redact(node, new HashSet<string>(options.SensitiveJsonProperties, StringComparer.OrdinalIgnoreCase)))
                        result = node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                }
            }
            catch (JsonException) { }
        }

        if (!truncate) return result;
        var limit = Math.Max(0, options.MaxDiagnosticBodyLength);
        return result.Length <= limit
            ? result
            : $"{result[..limit]}{Environment.NewLine}… [{result.Length - limit} characters truncated]";
    }

    private static bool LooksLikeJson(string content)
    {
        var trimmed = content.AsSpan().TrimStart();
        return !trimmed.IsEmpty && trimmed[0] is '{' or '[';
    }

    private static bool Redact(JsonNode node, HashSet<string> sensitive)
    {
        var changed = false;
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
                if (sensitive.Contains(property.Key))
                {
                    obj[property.Key] = "[REDACTED]";
                    changed = true;
                }
                else if (property.Value is not null) changed |= Redact(property.Value, sensitive);
        }
        else if (node is JsonArray array)
            foreach (var item in array) if (item is not null) changed |= Redact(item, sensitive);
        return changed;
    }
}
