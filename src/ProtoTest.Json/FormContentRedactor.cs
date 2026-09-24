namespace ProtoTest.Json;

using ProtoTest.Core;

/// <summary>
/// Redacts the values of form-urlencoded bodies, and chooses the redactor for the other common
/// non-JSON bodies: XML and multipart, which cannot be read as key-value pairs.
/// </summary>
internal static class FormContentRedactor
{
    public static string Redact(string content, HashSet<string> sensitive)
    {
        if (content.Length == 0 || sensitive.Count == 0) return content;
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('<')) return XmlContentRedactor.Redact(content, sensitive);
        if (content.Contains("Content-Disposition", StringComparison.OrdinalIgnoreCase))
            return MultipartContentRedactor.Redact(content, sensitive);
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
}
