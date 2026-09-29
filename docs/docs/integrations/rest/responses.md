---
sidebar_position: 3
title: Responses and assertions
description: "Assert on a RestResponse: status, headers, JSON shapes and typed bodies, with the body buffered so it can be read any number of times."
---

# Responses and assertions

Every verb returns a `RestResponse`. The body is already buffered, so you can read it as many times as you like.

## Asserting

`RestResponse` exposes two facades. `Should` asserts what must hold, `ShouldNot` asserts what must not. Both return the response, so assertions chain:

```csharp
using var response = await Proto.Context.Rest()
    .Body(new CreateOrderRequest("observability-seat", 12, 19.95m))
    .PostAsync("/api/orders");

response
    .Should.HaveHttpStatus(HttpStatusCode.Created)
    .Should.MatchShape(new
    {
        product = "observability-seat",
        quantity = 12,
        total = JsonValue.GreaterThan(200m),
        status = "pending"
    });

response.ShouldNot.HaveHttpStatus(HttpStatusCode.InternalServerError);
```

A mismatch fails with every error at once, each with its JSON path:

```
POST /api/orders - Shape mismatch failed with 2 error(s):
  • [$.total]: Expected greater than 200, but found '19.95'. (Expected: "greater than 200", Actual: '19.95')
  • [$.status]: Values did not match. (Expected: "pending", Actual: "cancelled")
```

```csharp
public RestShouldAssertions Should { get; }
public RestAssertions ShouldNot { get; }

public RestResponse HaveHttpStatus(HttpStatusCode expected);            // on RestAssertions
public RestResponse HaveContentType(string mediaType);                 // on RestAssertions
public RestResponse HaveHeader(string name);                           // on RestAssertions
public RestResponse HaveHeader(string name, string value);             // on RestAssertions
public RestResponse HaveCookie(string name);                           // on RestAssertions
public RestResponse HaveCookie(string name, string value);             // on RestAssertions
public RestResponse HaveRedirectLocation(string location);             // on RestAssertions
public RestResponse MatchShape(object expectedShape, JsonSerializerOptions? options = null);       // on RestShouldAssertions
public RestResponse MatchShape(object expectedShape, bool exact, JsonSerializerOptions? options = null);  // on RestShouldAssertions
```

`ShouldNot.HaveHttpStatus(expected)` asserts a different status. The same polarity applies to content type, header, cookie and redirect assertions. Shape is positive-only: `MatchShape` lives on the `Should` facade and `ShouldNot` has no shape form.

| Assertion | `Should` | `ShouldNot` |
| --- | --- | --- |
| status, content type, header, cookie, redirect | exact match | negated match |
| `MatchShape` | match | no form (positive-only) |

### Asserting in the call

A pending request can assert its response shape where the call is made. `ExpectAsync` awaits the response and runs the same `Should.MatchShape` the after-the-fact spelling runs, returning the response:

```csharp
using var response = await Proto.Context.Rest()
    .Body(new CreateOrderRequest("observability-seat", 12, 19.95m))
    .PostAsync("/api/orders")
    .ExpectAsync(new { id = JsonValue.GreaterThan(0), status = "pending" });
```

On a mismatch the response is disposed and the `RestAssertionException` is rethrown, so a failed call cannot leak a response. `exact: true` switches the shape to [exact mode](../../foundation/shape-matching.md#exact-matching).

### Status assertions

A status assertion records an `assert.http.status` operation (source `ProtoTest.Rest`, parented to the request) with the expected and actual status codes, the client identity and `assertion.negated` when it is the `ShouldNot` side. `RestStatusAssertionException` carries the expected and actual status, the body and whether it was negated. It appends the sanitized body up to the smaller of the two length caps.

### Content type, headers, cookies and redirects

Each of these records its own `assert.http.*` operation (parented to the request, with the Checks section and `assertion.negated` on the `ShouldNot` side), returns the response, and names the request in its failure:

```csharp
response.Should.HaveContentType("application/json");          // the media type, without parameters, case-insensitive
response.Should.HaveHeader("X-Correlation");                  // presence; either response or content headers
response.Should.HaveHeader("X-Correlation", "abc");           // any value of a multi-valued header, ordinal
response.Should.HaveCookie("session");                        // a Set-Cookie pair; cookie names are case-sensitive
response.Should.HaveCookie("session", "abc123");              // the value before the first attribute, ordinal
response.Should.HaveRedirectLocation("/orders/42");           // the Location header as it arrived, relative or absolute
```

Header and cookie values pass through the shared redaction rules before they reach a failure message or the trace, so a failing assertion on `Authorization` or `Set-Cookie` shows `[REDACTED]` instead of the secret. A failure reads, for example, `GET /orders/42 - Expected header 'X-Correlation' to have value 'abc', but it was def.`

### `MatchShape`

Describe the JSON you expect as an anonymous object. The short version of the rules:

- **Objects match partially.** Only the properties you list are checked; anything else in the response is ignored.
- **Arrays match exactly.** Same length, compared position by position.
- **Property names are case-insensitive** by default.
- **Values can be literals or constraints** such as `JsonValue.GreaterThan(0)`.
- **Every mismatch is reported at once**, each with its JSON path.

A failure names the request it was made against, the identifier the trace already carries (shown in the example above).

The failure is a `RestAssertionException` whose `InnerException` is the shared `JsonShapeMismatchException` (`Mismatches`, `MatchedProperties`), so the mismatch list stays inspectable.

Keep one copy of the exact-mode rule on the [shape matching page](../../foundation/shape-matching.md#exact-matching): exact mode flags any unlisted field.

The assertion records an `assert.json.shape` operation. It stores the expected and actual shapes, the match result and the mismatch list. On success it records an `http.contract.shape` observation carrying the request identifier, matched property paths, target type and status code, the input [OpenAPI coverage](../openapi.md) and [traffic coverage](../../observability/coverage.md#traffic-coverage-observed-but-unasserted) use. When `CaptureExpectedShapes` is on, the expected shape is attached as `rest-{n:00}-expected-shape` (`-02`, `-03` … for repeated assertions on one response).

The full rules and every available matcher are on the [Shape matching](../../foundation/shape-matching.md) page.

:::tip[Shape a whole array]
Because arrays are positional, a list assertion is precise:

```csharp
audit.Should.MatchShape(new[]
{
    new { action = "workspace.created", resource = JsonValue.NotNull() },
    new { action = "release.deployed", resource = JsonValue.NotNull() }
});
```
:::

## Reading

```csharp
T? ReadAsJson<T>(JsonSerializerOptions? options = null);
T? ReadAsJson<T>(string jsonPath, JsonSerializerOptions? options = null);
T  ReadRequired<T>(JsonSerializerOptions? options = null);
T  ReadRequired<T>(string jsonPath, JsonSerializerOptions? options = null);
T? ReadAsAnonymous<T>(T anonymousTypeDefinition, JsonSerializerOptions? options = null);
dynamic? ReadAsDynamic();
byte[] ReadAsBytes();
```

`ReadAsJson<T>` is case-insensitive by default and returns `default` for an empty body. A deserialization failure records an `http.response.deserialize` event with `target.type` and `content.length` (plus `json.path` for a path read), then rethrows; the required reads record the same failed event for an empty body or JSON `null`. `ReadAsAnonymous` exists purely for type inference: the argument's values are ignored:

```csharp
var created = response.ReadAsAnonymous(new { id = 0, status = "" })!;
Console.WriteLine(created.id);
```

A common pattern is assert-then-read, so the test fails with a useful message before you dereference anything. `ReadRequired<T>` does both: it throws `RestAssertionException` naming the request identifier when the body is empty or JSON `null`, instead of making you write `!`:

```csharp
var workspace = response
    .Should.HaveHttpStatus(HttpStatusCode.Created)
    .ReadRequired<WorkspaceResponse>();
```

### Reading one value by path

`ReadAsJson<T>(jsonPath)` reads a single value instead of a wrapper record:

```csharp
var id = response.ReadAsJson<int>("$.id");
var total = response.ReadRequired<decimal>("$.order.total");
var first = response.ReadAsJson<string>("items[0].sku");
```

The path subset is `$` for the root, dot members (`$.customer.id`) and zero-based array indices (`$.items[0].sku`); a leading member without `$` is accepted as `$.member`. Members match case-sensitively. Filters, wildcards, quoted names and slices are excluded.

A path that does not resolve throws `RestAssertionException` whose message starts with the request identifier and names the path, for example `GET /orders/42 - The JSON path '$.missing' did not match: the member 'missing' was not found.`; the shared `JsonPathException` stays reachable as `InnerException`. A value of the wrong type throws `JsonException`. `ReadRequired<T>(jsonPath)` additionally throws when the body is empty or the path holds JSON `null`, for every `T`, including value types, because the null check runs before the deserializer, and the nullable `ReadAsJson<T>(jsonPath)` returns `default` for an empty body.

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
