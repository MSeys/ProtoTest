---
sidebar_position: 3
title: Responses and assertions
---

# Responses and assertions

Every verb returns a `RestResponse`. The body is already buffered, so you can read it as many times as you like.

## Asserting

Both assertions return the response, so they chain:

```csharp
using var response = await Proto.Context.Rest()
    .Body(new CreateOrderRequest("observability-seat", 12, 19.95m))
    .PostAsync("/api/orders");

response
    .ShouldHaveStatus(HttpStatusCode.Created)
    .ShouldMatchShape(new
    {
        product = "observability-seat",
        quantity = 12,
        total = JsonValue.GreaterThan(200m),
        status = "pending"
    });
```

### `ShouldHaveStatus`

```csharp
RestResponse ShouldHaveStatus(HttpStatusCode expectedStatusCode);
```

On failure it throws `RestStatusAssertionException` with `ExpectedStatusCode`, `ActualStatusCode` and `ResponseBody`. The body is appended to the message (with sensitive values redacted), so a failing `400` tells you *why* without re-running anything.

### `ShouldMatchShape`

```csharp
RestResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null);
```

Describe the JSON you expect as an anonymous object. The short version of the rules:

- **Objects match partially.** Only the properties you list are checked; anything else in the response is ignored.
- **Arrays match exactly.** Same length, compared position by position.
- **Property names are case-insensitive** by default.
- **Values can be constraints**, not just literals — `JsonValue.GreaterThan(0)`, `JsonValue.NotNull()`, `JsonValue.Regex(...)` and more.
- **Every mismatch is reported at once**, each with its JSON path.

```
Shape mismatch failed with 2 error(s):
  • [$.total]: Expected greater than 200, but found '19.95'. (Expected: "greater than 200", Actual: '19.95')
  • [$.status]: Values did not match. (Expected: "pending", Actual: "cancelled")
```

The full rules and every available matcher are on the [Shape matching](../../advanced/json-shapes.md) page.

Shape assertions also do double duty: the property paths they match are what [OpenAPI coverage](../../advanced/coverage.md) uses to report which response fields your suite actually checked.

:::tip Shape a whole array
Because arrays are positional, a list assertion is precise:

```csharp
audit.ShouldMatchShape(new[]
{
    new { action = "workspace.created", resource = JsonValue.NotNull() },
    new { action = "release.deployed", resource = JsonValue.NotNull() }
});
```
:::

## Reading

```csharp
T? ReadAsJson<T>(JsonSerializerOptions? options = null);
T? ReadAsAnonymous<T>(T anonymousTypeDefinition, JsonSerializerOptions? options = null);
dynamic? ReadAsDynamic();
byte[] ReadAsBytes();
```

`ReadAsJson<T>` is case-insensitive by default and returns `default` for an empty body. `ReadAsAnonymous` exists purely for type inference — the argument's values are ignored:

```csharp
var created = response.ReadAsAnonymous(new { id = 0, status = "" })!;
Console.WriteLine(created.id);
```

A common pattern is assert-then-read, so the test fails with a useful message before you dereference anything:

```csharp
var workspace = response
    .ShouldHaveStatus(HttpStatusCode.Created)
    .ReadAsJson<WorkspaceResponse>()!;
```

## Raw access

| Member | Type |
| --- | --- |
| `StatusCode` | `HttpStatusCode` |
| `IsSuccessStatusCode` | `bool` |
| `Headers` | `HttpResponseHeaders` |
| `ContentHeaders` | `HttpContentHeaders` |
| `Content` | `string` |
| `ContentBytes` | `ReadOnlyMemory<byte>` |
| `ElapsedTime` | `TimeSpan` |
| `RawResponse` | `HttpResponseMessage` |
