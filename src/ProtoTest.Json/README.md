# ProtoTest.Json

Shared partial JSON shape matching and value constraints used by ProtoTest protocol
integrations. Object shapes are partial; arrays are position- and length-sensitive.

The package also owns JSON diagnostic redaction and truncation used by HTTP protocol
integrations.

```csharp
JsonShapeMatcher.AssertMatch(json, new
{
    id = JsonValue.NotNull(),
    name = JsonValue.StringContaining("Proto"),
    score = JsonValue.Between(80, 100)
});
```
