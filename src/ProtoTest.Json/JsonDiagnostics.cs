namespace ProtoTest.Json;

using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ProtoTest.Core;

/// <summary>
/// Options for diagnostic rendering: what is redacted, and how much of a body survives into the trace
/// and the reports.
/// </summary>
public class JsonDiagnosticOptions
{
    /// <summary>
    /// Whether sensitive property values are redacted before a body is written to evidence. On by
    /// default, because a diagnostic body is a copy of live traffic.
    /// </summary>
    public bool RedactSensitiveData { get; set; } = true;

    /// <summary>
    /// The longest body a diagnostic keeps. Beyond it the body is truncated, so one oversized response
    /// cannot dominate the artifact.
    /// </summary>
    public int MaxDiagnosticBodyLength { get; set; } = 64 * 1024;

    /// <summary>
    /// The property names redacted by default. Shared with the trace's state value formatter through
    /// <see cref="ProtoRedactionDefaults"/>, so diagnostics and state cannot disagree about what is
    /// sensitive.
    /// </summary>
    public List<string> SensitiveJsonProperties { get; set; } =
        [.. ProtoRedactionDefaults.SensitivePropertyNames];
}

/// <summary>Turns a captured body into evidence that is safe to keep and readable in the viewer.</summary>
public static class JsonDiagnosticSanitizer
{
    /// <summary>
    /// Renders a value as JSON and sanitizes it. A value that cannot be serialized as a whole degrades
    /// element by element, so one unserializable item does not cost the rest.
    /// </summary>
    public static string Serialize(object? value, JsonDiagnosticOptions? configured = null)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        try
        {
            return Sanitize(JsonSerializer.Serialize(value, options), configured);
        }
        catch (Exception exception)
        {
            return SerializeDegraded(value, exception, options);
        }
    }

    /// <summary>
    /// A value that cannot be serialized as a whole is degraded element by element, so one
    /// unserializable item does not cost the rest; a single value reports what it was instead.
    /// </summary>
    private static string SerializeDegraded(object? value, Exception exception, JsonSerializerOptions options)
    {
        if (value is IEnumerable sequence and not string)
        {
            var items = new List<string>();
            foreach (var item in sequence)
            {
                try
                {
                    items.Add(JsonSerializer.Serialize(item, options));
                }
                catch (Exception itemException)
                {
                    items.Add(Unavailable(item, itemException));
                }
            }

            return $"[{string.Join(",", items)}]";
        }

        return Unavailable(value, exception);
    }

    private static string Unavailable(object? value, Exception exception)
        => JsonSerializer.Serialize(new
        {
            unavailable = true,
            type = DisplayType(value?.GetType()),
            reason = exception.GetType().Name
        });

    /// <summary>
    /// The name a reader should see for a value that could not be serialized. Compiler-generated
    /// names (<c>&lt;&gt;z__ReadOnlyArray</c>, anonymous types) carry no meaning, so the shape they
    /// describe is reported instead.
    /// </summary>
    private static string? DisplayType(Type? type)
    {
        if (type is null) return null;
        if (type.IsArray) return $"{DisplayType(type.GetElementType())}[]";
        return type.Name.StartsWith('<')
            ? typeof(IEnumerable).IsAssignableFrom(type) ? "collection" : "anonymous type"
            : type.Name;
    }

    /// <summary>
    /// Redacts sensitive properties in a captured body and truncates it to the configured length. A
    /// body that is not JSON is scanned as form, multipart or XML content instead.
    /// </summary>
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
                catch (JsonException)
                {
                    // A truncated or otherwise malformed body is exactly where a secret survives: it
                    // still starts like JSON, so the text-level form redactor would not look at it.
                    result = RedactMalformedJson(result, sensitive);
                }
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

    /// <summary>
    /// Redacts the string values of sensitive keys in text that starts like JSON but does not parse.
    /// The value pattern accepts an unterminated string, so a body cut off mid-value is still covered.
    /// </summary>
    private static string RedactMalformedJson(string content, HashSet<string> sensitive)
    {
        if (sensitive.Count == 0) return content;
        var names = string.Join("|", sensitive.Select(Regex.Escape));
        var pattern = $"\"(?<key>{names})\"\\s*:\\s*\"[^\"]*\"?";
        return Regex.Replace(
            content,
            pattern,
            match => $"\"{match.Groups["key"].Value}\":\"{ProtoUriSanitizer.RedactedValue}\"",
            RegexOptions.IgnoreCase);
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
