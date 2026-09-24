namespace ProtoTest.Json;

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProtoTest.Core;

public class JsonDiagnosticOptions
{
    public bool RedactSensitiveData { get; set; } = true;
    public int MaxDiagnosticBodyLength { get; set; } = 64 * 1024;

    /// <summary>
    /// The property names redacted by default. Shared with the trace's state value formatter through
    /// <see cref="ProtoRedactionDefaults"/>, so diagnostics and state cannot disagree about what is
    /// sensitive.
    /// </summary>
    public List<string> SensitiveJsonProperties { get; set; } =
        [.. ProtoRedactionDefaults.SensitivePropertyNames];
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
        if (options.RedactSensitiveData)
        {
            var sensitive = new HashSet<string>(options.SensitiveJsonProperties, StringComparer.OrdinalIgnoreCase);
            if (LooksLikeJson(result))
            {
                // JsonDocument (unlike JsonNode) tolerates duplicate property names, so a body with two
                // sensitive keys is copied through the writer with every occurrence redacted instead of
                // crashing the diagnostics path.
                try
                {
                    using var document = JsonDocument.Parse(result);
                    if (ContainsSensitive(document.RootElement, sensitive))
                        result = WriteRedacted(document.RootElement, sensitive);
                }
                catch (JsonException) { }
            }
            else
            {
                // Not JSON: scan the common non-JSON bodies (form-urlencoded, multipart, XML) for
                // sensitive keys so they do not pass through raw.
                result = FormContentRedactor.Redact(result, sensitive);
            }
        }

        if (!truncate) return result;
        var limit = Math.Max(0, options.MaxDiagnosticBodyLength);
        return result.Length <= limit
            ? result
            : $"{result[..limit]}{Environment.NewLine}… [{result.Length - limit} characters truncated]";
    }

    /// <summary>Whether the content starts with a JSON object or array, ignoring leading whitespace.</summary>
    public static bool LooksLikeJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        var trimmed = content.AsSpan().TrimStart();
        return !trimmed.IsEmpty && trimmed[0] is '{' or '[';
    }

    private static bool ContainsSensitive(JsonElement element, HashSet<string> sensitive)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    if (sensitive.Contains(property.Name) || ContainsSensitive(property.Value, sensitive))
                        return true;
                return false;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    if (ContainsSensitive(item, sensitive))
                        return true;
                return false;
            default:
                return false;
        }
    }

    private static string WriteRedacted(JsonElement element, HashSet<string> sensitive)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteElement(writer, element, sensitive);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element, HashSet<string> sensitive)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (sensitive.Contains(property.Name))
                        writer.WriteStringValue(ProtoUriSanitizer.RedactedValue);
                    else
                        WriteElement(writer, property.Value, sensitive);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteElement(writer, item, sensitive);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }
}
