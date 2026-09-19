---
sidebar_position: 3
title: Responses and assertions
description: "Assert on a RestResponse: status, headers, JSON shapes and typed bodies, with the body buffered so it can be read any number of times."
---

# Responses and assertions

Every verb returns a `RestResponse`. The body is already buffered, so you can read it as many times as you like.

## Asserting

`RestResponse` exposes two facades. `Should` asserts what must hold, `ShouldNot` asserts what must not — both return the response, so assertions chain:

```csharp
using var response = await Proto.Context.Rest()
    .Body(new CreateOrderRequest("observability-seat", 12, 19.95m))
    .PostAsync("/api/orders");

response
    .Should.HaveHttpStatus(HttpStatusCode.Created)
    .ShouldMatchShape(new
    {
        product = "observability-seat",
        quantity = 12,
        total = JsonValue.GreaterThan(200m),
        status = "pending"
    });

response.ShouldNot.HaveHttpStatus(HttpStatusCode.InternalServerError);
```

```csharp
public RestAssertions Should { get; }
public RestAssertions ShouldNot { get; }

public RestResponse HaveHttpStatus(HttpStatusCode expected);            // on RestAssertions
public RestResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null);
```

`ShouldNot.HaveHttpStatus(expected)` asserts the status is **anything but** `expected`; shape assertions are positive-only and live on the response.

### Status assertions

A status assertion records an `assert.http.status` operation (source `ProtoTest.Rest`, parented to the request) with the expected and actual status codes, the client identity and `assertion.negated` when it is the `ShouldNot` side. `RestStatusAssertionException` carries `ExpectedStatusCode`, `ActualStatusCode`, `ResponseBody` and `Negated`, and appends the sanitized response body to its message — bounded by the smaller of `ProtoTest:Rest:Responses:MaxDiagnosticBodyLength` and the attachment options' cap — so a failing `400` tells you *why* without re-running anything.

### `ShouldMatchShape`

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

The assertion records an `assert.json.shape` operation with the expected type, the actual media type, the expected and actual shapes, and the matched properties or every mismatch (`matched.property_count`, `shape.mismatches`, `shape.mismatch_count`, `shape.result`). On success it records an `http.contract.shape` observation carrying the request identifier, matched property paths, target type and status code — the input [OpenAPI coverage](../openapi.md) uses. When `CaptureExpectedShapes` is on, the expected shape is attached as `rest-{n:00}-expected-shape` (`-02`, `-03` … for repeated assertions on one response).

The full rules and every available matcher are on the [Shape matching](../../foundation/shape-matching.md) page.

:::tip[Shape a whole array]
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

`ReadAsJson<T>` is case-insensitive by default and returns `default` for an empty body. A deserialization failure records an `http.response.deserialize` event with `target.type` and `content.length`, then rethrows. `ReadAsAnonymous` exists purely for type inference — the argument's values are ignored:

```csharp
var created = response.ReadAsAnonymous(new { id = 0, status = "" })!;
Console.WriteLine(created.id);
```

A common pattern is assert-then-read, so the test fails with a useful message before you dereference anything:

```csharp
var workspace = response
    .Should.HaveHttpStatus(HttpStatusCode.Created)
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
