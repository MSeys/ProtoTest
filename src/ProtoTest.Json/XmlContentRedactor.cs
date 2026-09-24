namespace ProtoTest.Json;

using System.Text;
using System.Text.RegularExpressions;
using ProtoTest.Core;

/// <summary>
/// Redacts attributes and element text whose names are sensitive in XML bodies. Tags are scanned one by
/// one: attributes are redacted by name inside the tag, and only text that belongs to a sensitive
/// element (or one of its descendants) is replaced. Sibling and unrelated elements are never touched,
/// and quoted attribute values are never read as markup.
/// </summary>
internal static class XmlContentRedactor
{
    private static readonly Regex TagPattern = new(
        "<(?<close>/?)(?<name>[A-Za-z_:][-A-Za-z0-9_:.]*)(?<attributes>(?:\"[^\"]*\"|'[^']*'|[^\"'>])*)>");

    public static string Redact(string content, HashSet<string> sensitive)
    {
        if (sensitive.Count == 0) return content;

        var builder = new StringBuilder(content.Length);
        var sensitiveDepth = 0;
        var stack = new Stack<bool>();
        var position = 0;
        foreach (Match tag in TagPattern.Matches(content))
        {
            if (tag.Index > position) AppendText(builder, content[position..tag.Index], sensitiveDepth > 0);
            AppendTag(builder, tag, sensitive);
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

        if (position < content.Length) AppendText(builder, content[position..], sensitiveDepth > 0);
        return builder.ToString();
    }

    private static void AppendText(StringBuilder builder, string text, bool redact)
        => builder.Append(redact ? ProtoUriSanitizer.RedactedValue : text);

    private static void AppendTag(StringBuilder builder, Match tag, HashSet<string> sensitive)
    {
        var attributes = tag.Groups["attributes"];
        builder.Append(tag.Value, 0, attributes.Index - tag.Index);
        AppendAttributes(builder, attributes.Value, sensitive);
        var suffixStart = attributes.Index - tag.Index + attributes.Length;
        builder.Append(tag.Value, suffixStart, tag.Length - suffixStart);
    }

    private static void AppendAttributes(StringBuilder builder, string attributes, HashSet<string> sensitive)
    {
        var index = 0;
        while (index < attributes.Length)
        {
            if (!IsNameStart(attributes[index])) { builder.Append(attributes[index++]); continue; }
            var nameStart = index;
            while (index < attributes.Length && IsNameCharacter(attributes[index])) index++;
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

    private static bool IsNameStart(char character) => char.IsAsciiLetter(character) || character is '_' or ':';

    private static bool IsNameCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '_' or ':' or '-' or '.';
}
