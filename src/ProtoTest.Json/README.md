# ProtoTest.Json

Partial JSON shape matching and `JsonValue` constraints, plus the diagnostics redaction engine — the matcher shared by REST, GraphQL, gRPC and messaging.

```bash
dotnet add package ProtoTest.Json
```

The package arrives transitively with `ProtoTest.Rest` and `ProtoTest.GraphQL`; reference it directly for a smaller integration that only needs the matcher.

## Quick start

```csharp
JsonShapeMatcher.AssertMatch(json, new
{
    id = JsonValue.NotNull(),
    name = JsonValue.StringContaining("Proto"),
    score = JsonValue.Between(80, 100)
});

// A mismatch throws JsonShapeMismatchException with every mismatch attached.
try
{
    JsonShapeMatcher.AssertMatch(json, new { status = "active" });
}
catch (JsonShapeMismatchException exception)
{
    foreach (var mismatch in exception.Mismatches)
    {
        Console.WriteLine(mismatch);
    }
}
```

## What it adds

- **Shape matcher** — `JsonShapeMatcher.AssertMatch(string | JsonElement, expected, options?)` returns the matched JSON paths; empty or invalid JSON throws `JsonDocumentAssertionException`.
- **Matching rules** — object shapes are partial, arrays are exact in length and position, property names are case-insensitive by default, and `[JsonPropertyName]`, naming policy and `[JsonIgnore]` are honoured.
- **Value constraints** — `JsonValue.Any`, `NotNull`, `Null`, `Regex`, `StringContaining`, `StringStartingWith`, `StringEndingWith`, `StringMatching`, `GreaterThan`, `LessThan`, `Between`, `OneOf` and `Matching`.
- **Shared traced assertion** — `ProtoShapeAssertion.Assert(...)` records the `assert.json.shape` operation with expected/actual shapes and the matched property list; consumers forward an observation on success.
- **Diagnostics** — `JsonDiagnosticOptions` defaults (`RedactSensitiveData`, `MaxDiagnosticBodyLength`, `SensitiveJsonProperties`) and `JsonDiagnosticSanitizer` used by the HTTP stack.

Extra JSON properties never fail a match, but arrays are length- and position-sensitive; numeric comparisons go through `decimal`/`double`, and describing an expected shape is capped at depth 16 and 4096 expanded containers.

## Learn more

- [Shape matching](https://prototest.dev/docs/foundation/shape-matching)
- [Diagnostics showcase](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/DiagnosticsShowcase.cs)
