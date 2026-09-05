namespace ProtoTest.Rest.Matching;

using System.Reflection;
using System.Text.Json;
using ProtoTest.Rest.Exceptions;

public static class ShapeMatcher
{
    public static void AssertMatch(string jsonContent, object expectedShape)
    {
        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            throw new ArgumentException("Cannot match shape against an empty or null response content.", nameof(jsonContent));
        }

        using var doc = JsonDocument.Parse(jsonContent);
        var mismatches = new List<ShapeMismatch>();

        MatchElement(doc.RootElement, expectedShape, "$", mismatches);

        if (mismatches.Count > 0)
        {
            throw new ShapeMismatchException(mismatches);
        }
    }

    private static void MatchElement(JsonElement actual, object? expected, string path, List<ShapeMismatch> mismatches)
    {
        // Case 1: Expected is an IValueMatcher constraint (Is.NotNull, Is.GreaterThan, etc.)
        if (expected is IValueMatcher matcher)
        {
            var rawValue = ExtractRawValue(actual);
            if (!matcher.Matches(rawValue, out var error))
            {
                mismatches.Add(new ShapeMismatch(path, error ?? "Value matcher failed.", expected, rawValue));
            }
            return;
        }

        // Case 2: Expected is Null
        if (expected is null)
        {
            if (actual.ValueKind != JsonValueKind.Null)
            {
                mismatches.Add(new ShapeMismatch(path, $"Expected NULL, but found '{actual.GetRawText()}'.", null, actual.GetRawText()));
            }
            return;
        }

        // Case 3: Expected is an Object / Anonymous Structure
        if (expected.GetType().IsClass && expected is not string)
        {
            if (actual.ValueKind != JsonValueKind.Object)
            {
                mismatches.Add(new ShapeMismatch(path, $"Expected JSON Object, but found '{actual.ValueKind}'.", "Object", actual.ValueKind));
                return;
            }

            var expectedProperties = expected.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in expectedProperties)
            {
                var expectedVal = prop.GetValue(expected);
                var propPath = $"{path}.{prop.Name}";

                if (TryGetJsonProperty(actual, prop.Name, out var actualProp))
                {
                    MatchElement(actualProp, expectedVal, propPath, mismatches);
                }
                else
                {
                    mismatches.Add(new ShapeMismatch(propPath, "Property was missing from the JSON response.", expectedVal, null));
                }
            }
            return;
        }

        // Case 4: Expected is a Primitive Exact Value (string, int, bool, etc.)
        var actualRaw = ExtractRawValue(actual);
        if (!EqualsPrimitive(actualRaw, expected))
        {
            mismatches.Add(new ShapeMismatch(path, $"Expected '{expected}', but found '{actualRaw ?? "NULL"}'.", expected, actualRaw));
        }
    }

    private static bool TryGetJsonProperty(JsonElement element, string propName, out JsonElement result)
    {
        foreach (var p in element.EnumerateObject())
        {
            if (string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase))
            {
                result = p.Value;
                return true;
            }
        }
        result = default;
        return false;
    }

    private static object? ExtractRawValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private static bool EqualsPrimitive(object? actual, object expected)
    {
        if (actual is null) return false;

        try
        {
            var convertedActual = Convert.ChangeType(actual, expected.GetType());
            return Equals(convertedActual, expected);
        }
        catch
        {
            return false;
        }
    }
}