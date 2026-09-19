namespace ProtoTest.Core;

using System.Text.Json;

/// <summary>
/// Caps long payloads before they become trace sections, so a trace stays a trace and not a dump.
/// JSON is pretty-printed before it is capped; anything else is capped as written.
/// </summary>
public static class ProtoTraceContent
{
    /// <summary>
    /// Pretty-prints <paramref name="content"/> when it is JSON and truncates it when it exceeds
    /// <paramref name="maxLength"/>, marking the cut the way ProtoTest's request preview does.
    /// </summary>
    public static string? Preview(string? content, int maxLength = 8000)
    {
        if (content is null)
        {
            return null;
        }

        var limit = Math.Max(0, maxLength);
        try
        {
            using var document = JsonDocument.Parse(content);
            var pretty = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return pretty.Length > limit ? $"{pretty[..limit]}\n…" : pretty;
        }
        catch (JsonException)
        {
            return content.Length > limit ? $"{content[..limit]}…" : content;
        }
    }
}
