namespace ProtoTest.Json;

using ProtoTest.Core;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

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
                result = RedactKeyValues(result, sensitive);
            }
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

    // The multipart scanner is deliberately not a regex: a hostile body full of unmatched quotes made
    // the previous pattern backtrack exponentially, and a value line that merely starts with "--" cut a
    // redacted value short. Lines are scanned once, a boundary is only believed when the line is a
    // terminator or the next line starts a part, and the value is replaced whole.
    private static string RedactMultipart(string content, HashSet<string> sensitive)
    {
        var lines = SplitMultipartLines(content);
        StringBuilder? builder = null;
        var copied = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            if (!HasSensitiveDispositionName(content, lines[index], sensitive)) continue;

            // The part's value begins after the blank line that ends its headers.
            var headerEnd = index + 1;
            while (headerEnd < lines.Count && !IsBlankLine(content, lines[headerEnd])) headerEnd++;
            if (headerEnd >= lines.Count) break;
            var valueStart = headerEnd + 1;
            if (valueStart >= lines.Count) continue;

            var valueEnd = valueStart;
            while (valueEnd < lines.Count && !IsMultipartBoundary(content, lines, valueEnd)) valueEnd++;
            if (valueEnd == valueStart) continue;

            builder ??= new StringBuilder(content.Length);
            var last = lines[valueEnd - 1];
            builder.Append(content, copied, lines[valueStart].Start - copied);
            builder.Append(ProtoUriSanitizer.RedactedValue);
            copied = last.Start + last.ContentLength;
            index = valueEnd - 1;
        }

        if (builder is null) return content;
        builder.Append(content, copied, content.Length - copied);
        return builder.ToString();
    }

    private readonly record struct MultipartLine(int Start, int ContentLength);

    private static List<MultipartLine> SplitMultipartLines(string content)
    {
        var lines = new List<MultipartLine>();
        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != '\n') continue;
            var length = index - start;
            if (length > 0 && content[index - 1] == '\r') length--;
            lines.Add(new MultipartLine(start, length));
            start = index + 1;
        }

        if (start < content.Length) lines.Add(new MultipartLine(start, content.Length - start));
        return lines;
    }

    private static string LineText(string content, MultipartLine line)
        => content.Substring(line.Start, line.ContentLength);

    private static bool IsBlankLine(string content, MultipartLine line)
        => LineText(content, line).AsSpan().Trim().Length == 0;

    private static bool HasSensitiveDispositionName(string content, MultipartLine line, HashSet<string> sensitive)
    {
        var text = LineText(content, line);
        if (!text.TrimStart().StartsWith("Content-Disposition", StringComparison.OrdinalIgnoreCase)) return false;

        var index = 0;
        while ((index = text.IndexOf("name=", index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var valueStart = index + "name=".Length;
            if (valueStart < text.Length && text[valueStart] is '"' or '\'')
            {
                var quote = text[valueStart];
                var valueEnd = text.IndexOf(quote, valueStart + 1);
                if (valueEnd > valueStart && sensitive.Contains(text[(valueStart + 1)..valueEnd])) return true;
                index = valueEnd > valueStart ? valueEnd : valueStart + 1;
                continue;
            }

            index = valueStart;
        }

        return false;
    }

    private static bool IsMultipartBoundary(string content, List<MultipartLine> lines, int index)
    {
        var text = LineText(content, lines[index]).TrimStart();
        if (!text.StartsWith("--", StringComparison.Ordinal)) return false;
        var boundary = text[2..].TrimEnd();
        // A terminator ends with "--"; the final line of the body is a boundary by definition.
        if (boundary.Length == 0 || boundary.EndsWith("--", StringComparison.Ordinal)) return true;
        if (index + 1 >= lines.Count) return true;

        // A line that merely starts with "--" is multipart data unless a part follows it.
        var next = LineText(content, lines[index + 1]).TrimStart();
        return next.Length == 0
            || next.StartsWith("Content-Disposition", StringComparison.OrdinalIgnoreCase)
            || next.StartsWith("Content-Type", StringComparison.OrdinalIgnoreCase)
            || next.StartsWith("Content-Transfer-Encoding", StringComparison.OrdinalIgnoreCase);
    }

    private static string RedactKeyValues(string content, HashSet<string> sensitive)
    {
        if (content.Length == 0 || sensitive.Count == 0) return content;
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('<')) return RedactXml(content, sensitive);
        if (content.Contains("Content-Disposition", StringComparison.OrdinalIgnoreCase))
            return RedactMultipart(content, sensitive);
        if (!LooksLikeForm(content)) return content;

        var segments = content.Split('&');
        for (var index = 0; index < segments.Length; index++)
        {
            var separator = segments[index].IndexOf('=');
            if (separator <= 0) continue;
            if (sensitive.Contains(Uri.UnescapeDataString(segments[index][..separator])))
                segments[index] = $"{segments[index][..separator]}={ProtoUriSanitizer.RedactedValue}";
        }

        return string.Join('&', segments);
    }

    private static bool LooksLikeForm(string content)
    {
        var sawPair = false;
        foreach (var segment in content.Split('&'))
        {
            if (segment.Length == 0) continue;
            var separator = segment.IndexOf('=');
            if (separator <= 0) return false;
            for (var index = 0; index < separator; index++)
            {
                var character = segment[index];
                if (!char.IsLetterOrDigit(character)
                    && character is not ('_' or '-' or '.' or '[' or ']' or '%' or '+'))
                {
                    return false;
                }
            }

            sawPair = true;
        }

        return sawPair;
    }

    private static string RedactXml(string content, HashSet<string> sensitive)
    {
        if (sensitive.Count == 0) return content;

        // Tags are scanned one by one: attributes are redacted by name inside the tag, and only text
        // that belongs to a sensitive element (or one of its descendants) is replaced. Sibling and
        // unrelated elements are never touched, and quoted attribute values are never read as markup.
        var builder = new StringBuilder(content.Length);
        var sensitiveDepth = 0;
        var stack = new Stack<bool>();
        var position = 0;
        foreach (Match tag in XmlTagPattern.Matches(content))
        {
            if (tag.Index > position) AppendXmlText(builder, content[position..tag.Index], sensitiveDepth > 0);
            AppendRedactedTag(builder, tag, sensitive);
            if (tag.Groups["close"].Length > 0)
            {
                if (stack.TryPop(out var wasSensitive) && wasSensitive) sensitiveDepth--;
            }
            else if (!tag.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                var isSensitive = sensitive.Contains(tag.Groups["name"].Value);
                if (isSensitive) sensitiveDepth++;
                stack.Push(isSensitive);
            }

            position = tag.Index + tag.Length;
        }

        if (position < content.Length) AppendXmlText(builder, content[position..], sensitiveDepth > 0);
        return builder.ToString();
    }

    private static void AppendXmlText(StringBuilder builder, string text, bool redact)
        => builder.Append(redact ? ProtoUriSanitizer.RedactedValue : text);

    private static readonly Regex XmlTagPattern = new(
        "<(?<close>/?)(?<name>[A-Za-z_:][-A-Za-z0-9_:.]*)(?<attributes>(?:\"[^\"]*\"|'[^']*'|[^\"'>])*)>");

    private static void AppendRedactedTag(StringBuilder builder, Match tag, HashSet<string> sensitive)
    {
        var attributes = tag.Groups["attributes"];
        builder.Append(tag.Value, 0, attributes.Index - tag.Index);
        AppendRedactedAttributes(builder, attributes.Value, sensitive);
        var suffixStart = attributes.Index - tag.Index + attributes.Length;
        builder.Append(tag.Value, suffixStart, tag.Length - suffixStart);
    }

    private static void AppendRedactedAttributes(StringBuilder builder, string attributes, HashSet<string> sensitive)
    {
        var index = 0;
        while (index < attributes.Length)
        {
            if (!IsXmlNameStart(attributes[index])) { builder.Append(attributes[index++]); continue; }
            var nameStart = index;
            while (index < attributes.Length && IsXmlNameCharacter(attributes[index])) index++;
            var name = attributes[nameStart..index];

            var afterName = index;
            while (index < attributes.Length && char.IsWhiteSpace(attributes[index])) index++;
            if (index >= attributes.Length || attributes[index] != '=')
            {
                builder.Append(attributes, nameStart, index - nameStart);
                continue;
            }

            index++;
            while (index < attributes.Length && char.IsWhiteSpace(attributes[index])) index++;
            if (index < attributes.Length && attributes[index] is '"' or '\'')
            {
                var quote = attributes[index++];
                var valueStart = index;
                while (index < attributes.Length && attributes[index] != quote) index++;
                builder.Append(attributes, nameStart, valueStart - nameStart);
                builder.Append(sensitive.Contains(name) ? ProtoUriSanitizer.RedactedValue : attributes[valueStart..index]);
                if (index < attributes.Length) builder.Append(quote);
                index++;
                continue;
            }

            var unquotedStart = index;
            while (index < attributes.Length && !char.IsWhiteSpace(attributes[index])) index++;
            builder.Append(attributes, nameStart, unquotedStart - nameStart);
            builder.Append(sensitive.Contains(name) ? ProtoUriSanitizer.RedactedValue : attributes[unquotedStart..index]);
        }
    }

    private static bool IsXmlNameStart(char character) => char.IsAsciiLetter(character) || character is '_' or ':';

    private static bool IsXmlNameCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '_' or ':' or '-' or '.';
}
