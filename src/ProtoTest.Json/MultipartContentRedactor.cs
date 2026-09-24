namespace ProtoTest.Json;

using System.Text;
using ProtoTest.Core;

/// <summary>
/// Redacts the values of sensitive parts in multipart bodies. The scanner is deliberately not a regex:
/// a hostile body full of unmatched quotes made the previous pattern backtrack exponentially, and a
/// value line that merely starts with "--" cut a redacted value short. Lines are scanned once, a
/// boundary is only believed when the line is a terminator or the next line starts a part, and the
/// value is replaced whole.
/// </summary>
internal static class MultipartContentRedactor
{
    public static string Redact(string content, HashSet<string> sensitive)
    {
        var lines = SplitLines(content);
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
            while (valueEnd < lines.Count && !IsBoundary(content, lines, valueEnd)) valueEnd++;
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

    private readonly record struct Line(int Start, int ContentLength);

    private static List<Line> SplitLines(string content)
    {
        var lines = new List<Line>();
        var start = 0;
        for (var index = 0; index < content.Length; index++)
        {
            if (content[index] != '\n') continue;
            var length = index - start;
            if (length > 0 && content[index - 1] == '\r') length--;
            lines.Add(new Line(start, length));
            start = index + 1;
        }

        if (start < content.Length) lines.Add(new Line(start, content.Length - start));
        return lines;
    }

    private static string LineText(string content, Line line)
        => content.Substring(line.Start, line.ContentLength);

    private static bool IsBlankLine(string content, Line line)
        => LineText(content, line).AsSpan().Trim().Length == 0;

    private static bool HasSensitiveDispositionName(string content, Line line, HashSet<string> sensitive)
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

    private static bool IsBoundary(string content, List<Line> lines, int index)
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
}
