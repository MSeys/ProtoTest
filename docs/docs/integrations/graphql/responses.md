---
sidebar_position: 3
title: Responses
description: "Assert on GraphQL responses: data shapes and errors, with the same shape matcher REST uses."
---

# Responses

`ExecuteAsync()`, `ExpectAsync()` and `SubscribeAsync()` return a `GraphQLResponse`. It's `IDisposable`; use `using var`.

GraphQL servers usually answer `200 OK` even when an operation fails, so the interesting part is the `errors` array, not the status code.

## Assertions

All of them return the response, so they chain.

```csharp
public GraphQLShouldAssertions Should { get; }
public GraphQLAssertions ShouldNot { get; }

public GraphQLResponse HaveHttpStatus(HttpStatusCode expected);                 // on GraphQLAssertions
public GraphQLResponse HaveNoErrors();                                          // on GraphQLAssertions
public GraphQLResponse HaveErrors();
public GraphQLResponse HaveError(string code);                                  // matches extensions.code, case-insensitive
public GraphQLResponse MatchShape(object expectedShape, JsonSerializerOptions? options = null);  // on GraphQLShouldAssertions
public GraphQLResponse MatchShape(object expectedShape, bool exact, JsonSerializerOptions? options = null);  // on GraphQLShouldAssertions
```

`ShouldNot.HaveHttpStatus(expected)` asserts the HTTP status is **anything but** `expected`. The error assertions honor the same polarity, so `ShouldNot.HaveErrors()` is the same check as `Should.HaveNoErrors()`. Shape is positive-only, so `MatchShape` lives on the `Should` facade and `ShouldNot` has no shape form.

```csharp
using var response = await Proto.Context.GraphQL()
    .WithoutAuth()
    .Query("me")
    .Select(new { id = Gql.Field })
    .ExecuteAsync();

response.Should.HaveErrors().Should.HaveError("UNAUTHORIZED");
```

### Error assertions

- `Should.HaveNoErrors()` fails if `Errors.Count > 0`.
- `Should.HaveErrors()` fails if the response has no errors.
- `Should.HaveError(code)` matches `GraphQLError.Code`, parsed from `extensions.code`, case-insensitively.

Each records its own operation (`assert.graphql.no_errors`, `assert.graphql.has_errors`, `assert.graphql.error_code` with `expected.error_code` and `actual.error_codes`), parented to the request, with an error count attribute. Failures throw `GraphQLAssertionException`. The old `ShouldHaveNoErrors()`/`ShouldHaveErrors()`/`ShouldHaveError(code)` response methods remain as obsolete shims.

### Shape assertions

`Should.MatchShape` uses the same rules as REST's: partial objects, exact arrays, `JsonValue` constraints. See [Shape matching](../../foundation/shape-matching.md). For shape-driven operations it compares against the **root field's value**; for fluent and raw operations, against the whole `data` object.

`MatchShape(shape, exact: true)` is the exhaustive form: a field present in the response that the shape does not mention is a mismatch naming that field. A value constraint mentions its whole subtree. The [shape matching page](../../foundation/shape-matching.md#exact-matching) has the rules.

The assertion records an `assert.json.shape` operation with the expected type and the operation identifier (`graphql.operation`), and on success records a `graphql.contract.shape` observation carrying the request identifier and the matched property paths.

A mismatch throws `GraphQLAssertionException` whose message starts with the operation identifier, keeping the shared `JsonShapeMismatchException`, and so the whole mismatch list, as `InnerException`:

```
query Orders - Shape mismatch failed with 1 error(s):
  • [$.value]: Values did not match. (Expected: '1', Actual: '2')
```

A response with no data (`data` missing or `null`, typically errors-only) records a failed `assert.json.shape` (`shape.result = mismatched`) and throws `GraphQLAssertionException("Expected GraphQL data, but the response did not contain data.")` rather than a shape mismatch.

## Reading

```csharp
T? ReadDataAs<T>(JsonSerializerOptions? options = null);
T? ReadDataAs<T>(string jsonPath, JsonSerializerOptions? options = null);
T  ReadRequired<T>(JsonSerializerOptions? options = null);
T  ReadRequired<T>(string jsonPath, JsonSerializerOptions? options = null);
```

`ReadDataAs` reads the same element `Should.MatchShape` compares against and is case-insensitive by default. It records a `graphql.response.deserialize` operation with `target.type` (plus `graphql.path` for a path read) and fails it with the exception it rethrows; a required path read fails the operation when the path holds JSON `null`.

`ReadDataAs<T>(jsonPath)` reads one value from the selected data instead of a wrapper record:

```csharp
var total = response.ReadDataAs<decimal>("$.order.total");
var first = response.ReadRequired<string>("items[0].sku");
```

The path subset is `$`, dot members and zero-based array indices; a leading member without `$` is accepted. Members match case-sensitively, and filters, wildcards, quoted names and slices are not part of the subset. A path that does not resolve throws `GraphQLAssertionException` whose message starts with the operation identifier and names the path, keeping the shared `JsonPathException` as `InnerException`. `ReadRequired<T>` throws `GraphQLAssertionException` naming the operation when the response has no data and `ReadRequired<T>(jsonPath)` when the path holds JSON `null`. The null check runs before the deserializer, so a value type reports the assertion exception too.

## Members

| Member | Type | Notes |
| --- | --- | --- |
| `Errors` | `IReadOnlyList<GraphQLError>` | `GraphQLError(Message, Path, Code, Extensions)`; the code comes from `extensions.code` |
| `HasErrors` / `HasData` | `bool` | |
| `Data` | `JsonElement?` | the full `data` object |
| `SelectedData` | `JsonElement?` | the root field's value for shape-driven operations, else the full `data` object |
| `Extensions` | `JsonElement?` | |
| `StatusCode` | `HttpStatusCode` | |
| `Content` | `string` | the raw body |
| `ElapsedTime` | `TimeSpan` | |
| `RawResponse` | `HttpResponseMessage` | |

## Protocol errors

A body that isn't JSON, or that has neither `data` nor `errors`, throws `GraphQLProtocolException` with the sanitized body in `ResponseContent`. That's distinct from a well-formed response that contains errors; those are for you to assert on.
