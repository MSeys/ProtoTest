namespace ProtoTest.Rest.Matching;

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProtoTest.Rest.Exceptions;
using ProtoTest.Rest.Internal;

internal static class ShapeMatcher
{
    public static IReadOnlyList<string> AssertMatch(
        string jsonContent,
        object expectedShape,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(expectedShape);
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            throw new RestJsonAssertionException(
                "Expected a JSON response, but the response body was empty.",
                jsonContent ?? string.Empty);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonContent);
        }
        catch (JsonException exception)
        {
            var diagnosticBody = RestDiagnosticSanitizer.SanitizeBody(
                jsonContent,
                "application/json",
                configuredOptions: null);
            throw new RestJsonAssertionException(
                $"Expected a valid JSON response, but parsing failed: {exception.Message}",
                diagnosticBody,
                exception);
        }

        using (document)
        {
            var mismatches = new List<ShapeMismatch>();
            var matchedProperties = new List<string>();

            MatchElement(document.RootElement, expectedShape, "$", mismatches, matchedProperties, options);

            if (mismatches.Count > 0)
            {
                throw new ShapeMismatchException(mismatches);
            }

            return matchedProperties;
        }
    }

    private static void MatchElement(
        JsonElement actual,
        object? expected,
        string path,
        List<ShapeMismatch> mismatches,
        List<string> matchedProperties,
        JsonSerializerOptions? options)
    {
        matchedProperties.Add(path);

        if (expected is IJsonValueMatcher matcher)
        {
            var rawValue = ExtractRawValue(actual);
            if (!matcher.Matches(rawValue, out var error))
            {
                mismatches.Add(new ShapeMismatch(path, error ?? "Value matcher failed.", matcher.Description, rawValue));
            }
            return;
        }

        if (expected is null)
        {
            if (actual.ValueKind != JsonValueKind.Null)
            {
                mismatches.Add(new ShapeMismatch(path, "Expected null.", null, ExtractRawValue(actual)));
            }
            return;
        }

        if (TryGetDictionary(expected, out var expectedProperties))
        {
            MatchObject(actual, expectedProperties, path, mismatches, matchedProperties, options);
            return;
        }

        if (expected is IEnumerable expectedItems && expected is not string)
        {
            MatchArray(actual, expectedItems.Cast<object?>().ToArray(), path, mismatches, matchedProperties, options);
            return;
        }

        if (!IsSimpleValue(expected.GetType()))
        {
            var properties = expected.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0)
                .Select(property => new KeyValuePair<string, object?>(
                    GetJsonPropertyName(property, options),
                    property.GetValue(expected)))
                .ToArray();

            MatchObject(actual, properties, path, mismatches, matchedProperties, options);
            return;
        }

        if (!EqualsScalar(actual, expected, out var actualValue))
        {
            mismatches.Add(new ShapeMismatch(path, "Values did not match.", expected, actualValue));
        }
    }

    private static void MatchObject(
        JsonElement actual,
        IEnumerable<KeyValuePair<string, object?>> expectedProperties,
        string path,
        List<ShapeMismatch> mismatches,
        List<string> matchedProperties,
        JsonSerializerOptions? options)
    {
        if (actual.ValueKind != JsonValueKind.Object)
        {
            mismatches.Add(new ShapeMismatch(path, $"Expected a JSON object, but found {actual.ValueKind}.", "object", ExtractRawValue(actual)));
            return;
        }

        foreach (var (name, expectedValue) in expectedProperties)
        {
            var propertyPath = $"{path}.{name}";
            if (TryGetJsonProperty(actual, name, options?.PropertyNameCaseInsensitive ?? true, out var actualProperty))
            {
                MatchElement(actualProperty, expectedValue, propertyPath, mismatches, matchedProperties, options);
            }
            else
            {
                mismatches.Add(new ShapeMismatch(propertyPath, "Property was missing from the JSON response.", expectedValue, null));
            }
        }
    }

    private static void MatchArray(
        JsonElement actual,
        IReadOnlyList<object?> expectedItems,
        string path,
        List<ShapeMismatch> mismatches,
        List<string> matchedProperties,
        JsonSerializerOptions? options)
    {
        if (actual.ValueKind != JsonValueKind.Array)
        {
            mismatches.Add(new ShapeMismatch(path, $"Expected a JSON array, but found {actual.ValueKind}.", "array", ExtractRawValue(actual)));
            return;
        }

        var actualItems = actual.EnumerateArray().ToArray();
        if (actualItems.Length != expectedItems.Count)
        {
            mismatches.Add(new ShapeMismatch(
                path,
                "Array lengths did not match.",
                expectedItems.Count,
                actualItems.Length));
        }

        var comparableCount = Math.Min(actualItems.Length, expectedItems.Count);
        for (var index = 0; index < comparableCount; index++)
        {
            MatchElement(
                actualItems[index],
                expectedItems[index],
                $"{path}[{index}]",
                mismatches,
                matchedProperties,
                options);
        }
    }

    private static bool TryGetDictionary(
        object expected,
        out IReadOnlyList<KeyValuePair<string, object?>> properties)
    {
        if (expected is IDictionary dictionary)
        {
            var result = new List<KeyValuePair<string, object?>>();
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Key is not string key)
                {
                    properties = [];
                    return false;
                }
                result.Add(new KeyValuePair<string, object?>(key, entry.Value));
            }

            properties = result;
            return true;
        }

        properties = [];
        return false;
    }

    private static string GetJsonPropertyName(PropertyInfo property, JsonSerializerOptions? options)
        => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
           ?? options?.PropertyNamingPolicy?.ConvertName(property.Name)
           ?? property.Name;

    private static bool TryGetJsonProperty(
        JsonElement element,
        string propertyName,
        bool caseInsensitive,
        out JsonElement result)
    {
        var comparison = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, comparison))
            {
                result = property.Value;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static bool IsSimpleValue(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsPrimitive
               || type.IsEnum
               || type == typeof(string)
               || type == typeof(decimal)
               || type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(DateOnly)
               || type == typeof(TimeOnly)
               || type == typeof(Guid)
               || type == typeof(Uri);
    }

    private static bool EqualsScalar(JsonElement actual, object expected, out object? actualValue)
    {
        actualValue = ExtractRawValue(actual);
        var expectedType = Nullable.GetUnderlyingType(expected.GetType()) ?? expected.GetType();

        if (expectedType == typeof(string) || expectedType == typeof(char) || expectedType == typeof(Uri))
        {
            return actual.ValueKind == JsonValueKind.String
                   && string.Equals(actual.GetString(), Convert.ToString(expected, CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        if (expectedType == typeof(bool))
        {
            return actual.ValueKind is JsonValueKind.True or JsonValueKind.False
                   && actual.GetBoolean() == (bool)expected;
        }

        if (IsNumeric(expectedType))
        {
            return actual.ValueKind == JsonValueKind.Number
                   && actual.TryGetDecimal(out var actualNumber)
                   && actualNumber == Convert.ToDecimal(expected, CultureInfo.InvariantCulture);
        }

        if (expectedType == typeof(Guid))
        {
            return actual.ValueKind == JsonValueKind.String
                   && Guid.TryParse(actual.GetString(), out var guid)
                   && guid == (Guid)expected;
        }

        if (expectedType == typeof(DateTime))
        {
            return actual.ValueKind == JsonValueKind.String
                   && actual.TryGetDateTime(out var dateTime)
                   && dateTime == (DateTime)expected;
        }

        if (expectedType == typeof(DateTimeOffset))
        {
            return actual.ValueKind == JsonValueKind.String
                   && actual.TryGetDateTimeOffset(out var dateTimeOffset)
                   && dateTimeOffset.Equals((DateTimeOffset)expected);
        }

        if (expectedType == typeof(DateOnly))
        {
            return actual.ValueKind == JsonValueKind.String
                   && DateOnly.TryParse(actual.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly)
                   && dateOnly == (DateOnly)expected;
        }

        if (expectedType == typeof(TimeOnly))
        {
            return actual.ValueKind == JsonValueKind.String
                   && TimeOnly.TryParse(actual.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly)
                   && timeOnly == (TimeOnly)expected;
        }

        if (expectedType.IsEnum)
        {
            if (actual.ValueKind == JsonValueKind.String)
            {
                return Enum.TryParse(expectedType, actual.GetString(), ignoreCase: true, out var parsed)
                       && Equals(parsed, expected);
            }

            return actual.ValueKind == JsonValueKind.Number
                   && actual.TryGetInt64(out var enumValue)
                   && enumValue == Convert.ToInt64(expected, CultureInfo.InvariantCulture);
        }

        return Equals(actualValue, expected);
    }

    private static bool IsNumeric(Type type)
        => Type.GetTypeCode(type) is TypeCode.Byte or TypeCode.SByte
            or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64
            or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
            or TypeCode.Decimal or TypeCode.Double or TypeCode.Single;

    private static object? ExtractRawValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };
}
