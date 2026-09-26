namespace ProtoTest.Json;

using System.Globalization;
using System.Text.Json;
using ProtoTest.Core;

/// <summary>
/// Reports that a JSON path did not resolve against a parsed document. The message names the path and
/// the reason; a protocol read wraps it in its own assertion exception, which adds the subject the read
/// was made against.
/// </summary>
public sealed class JsonPathException : ProtoAssertionException
{
    public JsonPathException(string path, string reason)
        : base($"The JSON path '{path}' did not match: {reason}")
    {
        Path = path;
        Reason = reason;
    }

    /// <summary>The path that failed, with the <c>$.member</c> prefix the resolver normalised.</summary>
    public string Path { get; }

    /// <summary>The reason the path did not resolve, without the path.</summary>
    public string Reason { get; }
}

/// <summary>
/// Resolves the JSON path subset the response reads accept: <c>$</c> for the document root, dot members
/// (<c>$.customer.id</c>) and zero-based array indices (<c>$.items[0].id</c>). A leading member without
/// <c>$</c> is accepted as <c>$.member</c>. Members match case-sensitively; filters, wildcards,
/// quoted names and slices are deliberately not part of the subset.
/// </summary>
public static class JsonPathResolver
{
    /// <summary>
    /// Returns the element <paramref name="path"/> points at, or throws <see cref="JsonPathException"/>
    /// naming the path and the reason it did not resolve.
    /// </summary>
    public static JsonElement Resolve(JsonElement root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Trim();
        if (normalized[0] != '$')
        {
            normalized = "$." + normalized;
        }

        var current = root;
        var index = 1;
        while (index < normalized.Length)
        {
            if (normalized[index] == '.')
            {
                index++;
                var start = index;
                while (index < normalized.Length && normalized[index] != '.' && normalized[index] != '[')
                {
                    index++;
                }

                if (index == start)
                {
                    throw new JsonPathException(normalized, "a member name was expected after '.'.");
                }

                var name = normalized[start..index];
                if (current.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonPathException(
                        normalized,
                        $"the member '{name}' cannot be read from a JSON {Kind(current)}.");
                }

                if (!TryMember(current, name, out var member))
                {
                    throw new JsonPathException(normalized, MissingMember(current, name));
                }

                current = member;
                continue;
            }

            if (normalized[index] == '[')
            {
                var close = normalized.IndexOf(']', index + 1);
                if (close < 0)
                {
                    throw new JsonPathException(normalized, "a closing ']' was expected.");
                }

                var text = normalized[(index + 1)..close];
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var arrayIndex))
                {
                    throw new JsonPathException(
                        normalized,
                        $"the index '[{text}]' is not a non-negative integer; only '[n]' is supported.");
                }

                if (current.ValueKind != JsonValueKind.Array)
                {
                    throw new JsonPathException(
                        normalized,
                        $"the index [{arrayIndex}] cannot be read from a JSON {Kind(current)}.");
                }

                var length = current.GetArrayLength();
                if (arrayIndex >= length)
                {
                    throw new JsonPathException(
                        normalized,
                        $"the index [{arrayIndex}] is outside the array's {length} element(s).");
                }

                current = current[arrayIndex];
                index = close + 1;
                continue;
            }

            throw new JsonPathException(normalized, $"a member name or index was expected, but found '{normalized[index]}'.");
        }

        return current;
    }

    private static bool TryMember(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.Ordinal))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    // A differing-case member is the common mistake, so naming it turns a silent miss into a fix.
    private static string MissingMember(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return $"the member '{name}' was not found; the document has '{property.Name}' and members match case-sensitively.";
            }
        }

        return $"the member '{name}' was not found.";
    }

    private static string Kind(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "value"
    };
}
