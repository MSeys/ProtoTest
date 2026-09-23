namespace ProtoTest.Json;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProtoTest.Core;

public sealed record JsonShapeMismatch(string PropertyPath, string Reason, object? Expected, object? Actual)
{
    public override string ToString()
        => $"  • [{PropertyPath}]: {Reason} (Expected: {Format(Expected)}, Actual: {Format(Actual)})";

    private static string Format(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        _ => $"'{value}'"
    };
}

public sealed class JsonShapeMismatchException(
    IReadOnlyList<JsonShapeMismatch> mismatches,
    IReadOnlyList<string>? matchedProperties = null)
    : ProtoAssertionException(BuildMessage(mismatches))
{
    public IReadOnlyList<JsonShapeMismatch> Mismatches { get; } = [.. mismatches];
    public IReadOnlyList<string> MatchedProperties { get; } = [.. matchedProperties ?? []];
    private static string BuildMessage(IReadOnlyList<JsonShapeMismatch> mismatches)
        => $"Shape mismatch failed with {mismatches.Count} error(s):{Environment.NewLine}" +
           string.Join(Environment.NewLine, mismatches.Select(m => m.ToString()));
}

public sealed class JsonDocumentAssertionException : ProtoAssertionException
{
    public JsonDocumentAssertionException(string message, string content) : base(message) => Content = content;
    public JsonDocumentAssertionException(string message, string content, Exception innerException) : base(message, innerException) => Content = content;
    public string Content { get; }
}

public static class JsonShapeMatcher
{
    public static IReadOnlyList<string> AssertMatch(string content, object? expected, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new JsonDocumentAssertionException("Expected JSON, but the content was empty.", content ?? string.Empty);
        try
        {
            using var document = JsonDocument.Parse(content);
            return AssertMatch(document.RootElement, expected, options);
        }
        catch (JsonException exception)
        {
            throw new JsonDocumentAssertionException($"Expected valid JSON, but parsing failed: {exception.Message}", content, exception);
        }
    }

    public static IReadOnlyList<string> AssertMatch(JsonElement actual, object? expected, JsonSerializerOptions? options = null)
    {
        var mismatches = new List<JsonShapeMismatch>();
        var matched = new List<string>();
        Match(actual, expected, "$", mismatches, matched, options);
        if (mismatches.Count > 0) throw new JsonShapeMismatchException(mismatches, matched);
        return matched;
    }

    private static void Match(JsonElement actual, object? expected, string path,
        List<JsonShapeMismatch> mismatches, List<string> matched, JsonSerializerOptions? options)
    {
        var mismatchCount = mismatches.Count;
        if (expected is IJsonValueMatcher matcher)
        {
            var raw = Raw(actual);
            if (!matcher.Matches(raw, out var error)) mismatches.Add(new(path, error ?? "Value constraint failed.", matcher.Description, raw));
            if (mismatches.Count == mismatchCount) matched.Add(path);
            return;
        }
        if (expected is null)
        {
            if (actual.ValueKind != JsonValueKind.Null) mismatches.Add(new(path, "Expected null.", null, Raw(actual)));
            if (mismatches.Count == mismatchCount) matched.Add(path);
            return;
        }
        if (expected is IEnumerable sequence and not string and not IDictionary)
        {
            if (actual.ValueKind != JsonValueKind.Array) { mismatches.Add(new(path, "Expected an array.", expected, Raw(actual))); return; }
            var expectedItems = sequence.Cast<object?>().ToArray();
            var actualItems = actual.EnumerateArray().ToArray();
            if (expectedItems.Length != actualItems.Length) mismatches.Add(new(path, "Array lengths did not match.", expectedItems.Length, actualItems.Length));
            for (var index = 0; index < Math.Min(expectedItems.Length, actualItems.Length); index++)
                Match(actualItems[index], expectedItems[index], $"{path}[{index}]", mismatches, matched, options);
            if (mismatches.Count == mismatchCount) matched.Add(path);
            return;
        }
        if (TryProperties(expected, options, out var properties))
        {
            if (actual.ValueKind != JsonValueKind.Object) { mismatches.Add(new(path, "Expected an object.", expected, Raw(actual))); return; }
            foreach (var (name, value) in properties)
            {
                var found = TryProperty(actual, name, options?.PropertyNameCaseInsensitive ?? true, out var property);
                if (!found) mismatches.Add(new($"{path}.{name}", "Property was missing from the JSON response.", value, null));
                else Match(property, value, $"{path}.{name}", mismatches, matched, options);
            }
            if (mismatches.Count == mismatchCount) matched.Add(path);
            return;
        }
        if (!ScalarEquals(actual, expected, out var actualValue))
            mismatches.Add(new(path, "Values did not match.", expected, actualValue));
        else
            matched.Add(path);
    }

    internal static bool TryProperties(object value, JsonSerializerOptions? options, out IReadOnlyList<KeyValuePair<string, object?>> properties)
    {
        if (value is IDictionary dictionary)
        {
            var entries = new List<KeyValuePair<string, object?>>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is null) { properties = []; return false; }
                // JSON object names are strings, so a non-string key is compared through its
                // invariant string form, mirroring how the expected shape is described in the trace.
                entries.Add(new(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty, entry.Value));
            }
            properties = entries;
            return true;
        }
        var type = value.GetType();
        if (JsonScalarTypes.IsScalar(type))
        { properties = []; return false; }
        properties = ProtoJsonPropertyProjection.Read(type, value, options?.PropertyNamingPolicy)
            .Select(property => new KeyValuePair<string, object?>(property.Name, property.Value))
            .ToArray();
        return true;
    }

    private static bool TryProperty(JsonElement element, string name, bool caseInsensitive, out JsonElement result)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            { result = property.Value; return true; }
        result = default;
        return false;
    }

    private static bool ScalarEquals(JsonElement actual, object expected, out object? actualValue)
    {
        actualValue = Raw(actual);
        var expectedType = Nullable.GetUnderlyingType(expected.GetType()) ?? expected.GetType();
        if (expectedType == typeof(string) || expectedType == typeof(char) || expectedType == typeof(Uri))
            return actual.ValueKind == JsonValueKind.String &&
                   string.Equals(actual.GetString(), Convert.ToString(expected, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        if (expectedType == typeof(bool))
            return actual.ValueKind is JsonValueKind.True or JsonValueKind.False && actual.GetBoolean() == (bool)expected;
        if (expectedType.IsEnum)
            return actual.ValueKind == JsonValueKind.String
                ? Enum.TryParse(expectedType, actual.GetString(), true, out var parsed) && Equals(parsed, expected)
                : actual.ValueKind == JsonValueKind.Number && actual.TryGetInt64(out var enumValue) && enumValue == Convert.ToInt64(expected, CultureInfo.InvariantCulture);
        if (JsonScalarTypes.IsNumeric(expectedType))
            return actual.ValueKind == JsonValueKind.Number && NumbersEqual(actual, expected);
        if (expectedType == typeof(Guid))
            return actual.ValueKind == JsonValueKind.String && Guid.TryParse(actual.GetString(), out var guid) && guid == (Guid)expected;
        if (expectedType == typeof(DateTime))
            return actual.ValueKind == JsonValueKind.String && actual.TryGetDateTime(out var dateTime) && dateTime == (DateTime)expected;
        if (expectedType == typeof(DateTimeOffset))
            return actual.ValueKind == JsonValueKind.String && actual.TryGetDateTimeOffset(out var dateTimeOffset) && dateTimeOffset.Equals((DateTimeOffset)expected);
        if (expectedType == typeof(DateOnly))
            return actual.ValueKind == JsonValueKind.String && DateOnly.TryParse(actual.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly) && dateOnly == (DateOnly)expected;
        if (expectedType == typeof(TimeOnly))
            return actual.ValueKind == JsonValueKind.String && TimeOnly.TryParse(actual.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly) && timeOnly == (TimeOnly)expected;
        return Equals(actualValue, expected);
    }

    /// <summary>
    /// Compares a JSON number against an expected numeric value without ever throwing: decimal first
    /// for exactness, then double for numbers outside the decimal range, then no match.
    /// </summary>
    private static bool NumbersEqual(JsonElement actual, object expected)
    {
        if (actual.TryGetDecimal(out var actualDecimal) && TryToDecimal(expected, out var expectedDecimal))
            return actualDecimal == expectedDecimal;
        if (actual.TryGetDouble(out var actualDouble) && TryToDouble(expected, out var expectedDouble))
            return actualDouble == expectedDouble;
        return false;
    }

    private static bool TryToDecimal(object value, out decimal result)
    {
        try
        {
            result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static bool TryToDouble(object value, out double result)
    {
        try
        {
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    private static object? Raw(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => RawNumber(element),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };

    private static object RawNumber(JsonElement element)
    {
        if (element.TryGetDecimal(out var decimalValue)) return decimalValue;
        if (element.TryGetDouble(out var doubleValue)) return doubleValue;
        return element.GetRawText();
    }
}
