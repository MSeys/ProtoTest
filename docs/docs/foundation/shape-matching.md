---
sidebar_position: 6
title: Shape matching
description: "Describe the JSON you expect with an anonymous object: partial, nested, with value constraints, and every mismatch reported at once with its path."
---

# Shape matching

REST's `ShouldMatchShape` and GraphQL's `ShouldMatchData` / `ExpectAsync` / `ExpectNextAsync` all use the same matcher from `ProtoTest.Json`. You describe the JSON you expect with an anonymous object, and the matcher compares.

```csharp
response.ShouldMatchShape(new
{
    id = JsonValue.GreaterThan(0),
    status = "pending",
    customer = new { email = JsonValue.StringEndingWith("@example.test") },
    lines = new[]
    {
        new { product = "notebook", quantity = 2 },
        new { product = "pen", quantity = JsonValue.Between(1, 10) }
    }
});
```

## The rules

### Objects are partial

Only the properties you list are checked. Everything else in the JSON is ignored — so a shape only states what the test is about, and new fields in the API don't break it.

A listed property that's missing fails with *"Property was missing from the JSON response."* A non-object where you expected one fails with *"Expected an object."*

### Arrays are exact

An array must have **the same length** and each element is compared **by position**. A length difference is reported, and the elements that do line up are still compared, so you see every problem at once.

If you only care that a list isn't empty, use a constraint instead of an array:

```csharp
users = JsonValue.NotNull()
```

Anything enumerable counts as an array — `List<T>`, LINQ results, arrays — except strings and dictionaries. This makes expected lists easy to compute:

```csharp
users = createdUsers
    .Select(user => new { user.Id, user.Email, user.Role })
    .OrderBy(user => user.Id, StringComparer.Ordinal)
    .ToArray()
```

### Rules apply at every depth

Nested objects are partial, nested arrays are exact, all the way down.

### Names are case-insensitive

`workspaceCount` matches `WorkspaceCount` in the JSON. Names come from `[JsonPropertyName]` if present, otherwise the naming policy in your `JsonSerializerOptions`, otherwise the C# name. Pass options with `PropertyNameCaseInsensitive = false` to make matching strict.

String **values** are compared exactly and case-sensitively.

### Values are compared by type

| Expected | Matches |
| --- | --- |
| number | any JSON number with the same decimal value — `12` matches `12.0` |
| `string` | the same string |
| `bool` | `true` / `false` |
| enum | its name (case-insensitive) or its numeric value |
| `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `Uri` | a JSON string that parses to the same value |
| `null` | JSON `null` |
| dictionary with string keys | an object, like an anonymous type |

### Every mismatch is reported

The matcher collects all mismatches, then throws one `JsonShapeMismatchException`:

```
Shape mismatch failed with 3 error(s):
  • [$.lines]: Array lengths did not match. (Expected: '2', Actual: '3')
  • [$.status]: Values did not match. (Expected: "pending", Actual: "cancelled")
  • [$.customer.email]: Expected ends with "@example.test", but found 'jane@example.org'. (Expected: "ends with "@example.test"", Actual: "jane@example.org")
```

```csharp
public sealed class JsonShapeMismatchException : ProtoAssertionException
{
    IReadOnlyList<JsonShapeMismatch> Mismatches { get; }      // (PropertyPath, Reason, Expected, Actual)
    IReadOnlyList<string> MatchedProperties { get; }
}
```

Empty or invalid JSON throws `JsonDocumentAssertionException` instead, carrying the content.

## Constraints

`JsonValue` gives you values that match by rule rather than equality:

| Constraint | Matches |
| --- | --- |
| `JsonValue.Any()` | anything, including `null` — the property just has to exist |
| `JsonValue.NotNull()` | anything except `null` |
| `JsonValue.Null()` | `null` |
| `JsonValue.GreaterThan(x)` | `> x` |
| `JsonValue.GreaterThanOrEqualTo(x)` | `>= x` |
| `JsonValue.LessThan(x)` | `< x` |
| `JsonValue.LessThanOrEqualTo(x)` | `<= x` |
| `JsonValue.Between(min, max)` | `min <= value <= max` |
| `JsonValue.OneOf(a, b, …)` | equal to one of the values |
| `JsonValue.StringContaining(s, comparison?)` | contains `s` |
| `JsonValue.StringStartingWith(s, comparison?)` | starts with `s` |
| `JsonValue.StringEndingWith(s, comparison?)` | ends with `s` |
| `JsonValue.Regex(pattern, options?)` | matches the regular expression |
| `JsonValue.StringMatching(predicate, description)` | a string satisfying your predicate |
| `JsonValue.Matching(predicate, description)` | any value satisfying your predicate |

String comparisons default to `StringComparison.Ordinal`.

Comparison constraints convert the JSON value to the type you passed. A value that can't be converted **doesn't match** — `JsonValue.LessThan(10)` against `"not-a-number"` is a mismatch, not an exception. So use `200m` rather than `200` when the value is a decimal amount.

`Matching` receives the raw value: a `string`, a `long` or `decimal`, a `bool`, `null`, or raw JSON text for objects and arrays. `StringMatching` receives the value as a `string`, or `null` when it isn't one.

```csharp
createdAt = JsonValue.StringMatching(
    value => DateTimeOffset.TryParse(value, out var at) && at > DateTimeOffset.UtcNow.AddMinutes(-5),
    "a timestamp from the last five minutes")
```

The description is what appears as *Expected* when it fails, so write it for the reader.

## Custom constraints

Implement `IJsonValueMatcher` for anything reusable:

```csharp
public interface IJsonValueMatcher
{
    string Description { get; }
    bool Matches(object? actual, out string? errorMessage);
}
```

```csharp
public sealed record IsoCurrency : IJsonValueMatcher
{
    public string Description => "an ISO 4217 currency code";

    public bool Matches(object? actual, out string? errorMessage)
    {
        if (actual is string { Length: 3 } code && code.All(char.IsAsciiLetterUpper))
        {
            errorMessage = null;
            return true;
        }

        errorMessage = $"Expected {Description}, but found '{actual}'.";
        return false;
    }
}
```

```csharp
response.ShouldMatchShape(new { total = new { currency = new IsoCurrency() } });
```

In GraphQL shapes, any `IJsonValueMatcher` is also treated as a leaf field when building the selection set.

## Using the matcher directly

```csharp
IReadOnlyList<string> matched = JsonShapeMatcher.AssertMatch(json, expectedShape);
IReadOnlyList<string> matched = JsonShapeMatcher.AssertMatch(jsonElement, expectedShape, options);
```

It returns the JSON paths that matched — the same list [OpenAPI coverage](../observability/coverage.md) uses.
