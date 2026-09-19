namespace ProtoTest.GraphQL.Internal;

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ProtoTest.Json;

/// <summary>
/// Redacts string literals assigned inline to sensitive argument or input-object field names in a
/// GraphQL document, for example <c>password: "hunter2"</c>. The scan is token-aware so names inside
/// strings, comments, aliases and <c>$variable</c> references are never rewritten. Redacted text is
/// only used for recorded documents and diagnostics; the request actually sent is never modified.
/// </summary>
internal static class GraphQLDocumentRedactor
{
    private const string RedactedLiteral = "\"[REDACTED]\"";

    /// <summary>Redacts a document using the configured rules; a missing options object uses the defaults.</summary>
    public static string Redact(string document, JsonDiagnosticOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);
        var effective = options ?? new JsonDiagnosticOptions();
        return effective.RedactSensitiveData
            ? Redact(document, effective.SensitiveJsonProperties)
            : document;
    }

    /// <summary>Redacts a document using the supplied sensitive argument and input-field names.</summary>
    public static string Redact(string document, IReadOnlyCollection<string>? sensitiveNames)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (sensitiveNames is null || sensitiveNames.Count == 0) return document;
        var sensitive = new HashSet<string>(sensitiveNames, StringComparer.OrdinalIgnoreCase);

        StringBuilder? builder = null;
        var copied = 0;
        var index = 0;
        while (index < document.Length)
        {
            var current = document[index];
            if (current == '#')
            {
                index = SkipToLineEnd(document, index);
                continue;
            }
            if (current == '"')
            {
                index = SkipString(document, index);
                continue;
            }
            if (!IsNameStart(current))
            {
                index++;
                continue;
            }

            var nameStart = index;
            index++;
            while (index < document.Length && IsNamePart(document[index])) index++;
            if (!sensitive.Contains(document[nameStart..index])) continue;

            var valueStart = SkipTrivia(document, index);
            if (valueStart >= document.Length || document[valueStart] != ':') continue;
            valueStart = SkipTrivia(document, valueStart + 1);
            if (valueStart >= document.Length || document[valueStart] != '"') continue;

            var valueEnd = SkipString(document, valueStart);
            builder ??= new StringBuilder(document.Length);
            builder.Append(document, copied, valueStart - copied);
            builder.Append(RedactedLiteral);
            copied = valueEnd;
            index = valueEnd;
        }

        if (builder is null) return document;
        builder.Append(document, copied, document.Length - copied);
        return builder.ToString();
    }

    /// <summary>Redacts the <c>query</c> member of a serialized GraphQL request envelope.</summary>
    public static string RedactEnvelope(string envelopeJson, JsonDiagnosticOptions? options)
    {
        ArgumentNullException.ThrowIfNull(envelopeJson);
        var effective = options ?? new JsonDiagnosticOptions();
        if (!effective.RedactSensitiveData || effective.SensitiveJsonProperties.Count == 0) return envelopeJson;
        try
        {
            if (JsonNode.Parse(envelopeJson) is not JsonObject envelope) return envelopeJson;
            if (envelope["query"] is not System.Text.Json.Nodes.JsonValue query || !query.TryGetValue<string>(out var document)) return envelopeJson;
            var redacted = Redact(document, effective.SensitiveJsonProperties);
            if (string.Equals(redacted, document, StringComparison.Ordinal)) return envelopeJson;
            envelope["query"] = redacted;
            return envelope.ToJsonString();
        }
        catch (JsonException)
        {
            return envelopeJson;
        }
    }

    private static int SkipToLineEnd(string text, int index)
    {
        while (index < text.Length && text[index] is not ('\n' or '\r')) index++;
        return index;
    }

    private static int SkipTrivia(string text, int index)
    {
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '#')
            {
                index = SkipToLineEnd(text, index);
                continue;
            }
            if (current == ',' || char.IsWhiteSpace(current) || current == '\uFEFF')
            {
                index++;
                continue;
            }
            break;
        }
        return index;
    }

    /// <summary>Returns the index just past the string literal that starts at <paramref name="start"/>.</summary>
    private static int SkipString(string text, int start)
    {
        if (start + 2 < text.Length && text[start + 1] == '"' && text[start + 2] == '"')
        {
            // Block string: only \""" escapes, and """ closes it.
            var index = start + 3;
            while (index < text.Length)
            {
                if (text[index] == '\\' && index + 3 < text.Length
                    && text[index + 1] == '"' && text[index + 2] == '"' && text[index + 3] == '"')
                {
                    index += 4;
                    continue;
                }
                if (text[index] == '"' && index + 2 < text.Length
                    && text[index + 1] == '"' && text[index + 2] == '"')
                {
                    return index + 3;
                }
                index++;
            }
            return text.Length;
        }

        var current = start + 1;
        while (current < text.Length)
        {
            if (text[current] == '\\')
            {
                current += 2;
                continue;
            }
            if (text[current] == '"') return current + 1;
            current++;
        }
        return text.Length;
    }

    private static bool IsNameStart(char value)
        => value == '_' || char.IsAsciiLetter(value);

    private static bool IsNamePart(char value)
        => value == '_' || char.IsAsciiLetterOrDigit(value);
}
