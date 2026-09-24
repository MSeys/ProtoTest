---
sidebar_position: 3
title: Responses
description: "Assert on GraphQL responses — data shapes and errors — with the same shape matcher REST uses."
---

# Responses

`ExecuteAsync()`, `ExpectAsync()` and `SubscribeAsync()` return a `GraphQLResponse`. It's `IDisposable` — use `using var`.

GraphQL servers usually answer `200 OK` even when an operation fails, so the interesting part is the `errors` array, not the status code.

## Assertions

All of them return the response, so they chain.

```csharp
public GraphQLAssertions Should { get; }
public GraphQLAssertions ShouldNot { get; }

public GraphQLResponse HaveHttpStatus(HttpStatusCode expected);                 // on GraphQLAssertions
public GraphQLResponse ShouldHaveNoErrors();
public GraphQLResponse ShouldHaveErrors();
public GraphQLResponse ShouldHaveError(string code);                            // matches extensions.code, case-insensitive
public GraphQLResponse ShouldMatchShape(object expectedShape, JsonSerializerOptions? options = null);
```

`ShouldNot.HaveHttpStatus(expected)` asserts the HTTP status is **anything but** `expected`. The error and shape assertions are positive-only, so there is no negated form for them.

```csharp
using var response = await Proto.Context.GraphQL()
    .WithoutAuth()
    .Query("me")
    .Select(new { id = Gql.Field })
    .ExecuteAsync();

response.ShouldHaveErrors().ShouldHaveError("UNAUTHORIZED");
```

### Error assertions

- `ShouldHaveNoErrors()` fails if `Errors.Count > 0`.
- `ShouldHaveErrors()` fails if the response has no errors.
- `ShouldHaveError(code)` matches `GraphQLError.Code`, parsed from `extensions.code`, case-insensitively.

Each records its own operation — `assert.graphql.no_errors`, `assert.graphql.has_errors`, `assert.graphql.error_code` (with `expected.error_code` and `actual.error_codes`) — parented to the request, with an error count attribute. Failures throw `GraphQLAssertionException`.

### Shape assertions

`ShouldMatchShape` uses the same rules as REST's — partial objects, exact arrays, `JsonValue` constraints — see [Shape matching](../../foundation/shape-matching.md). For shape-driven operations it compares against the **root field's value**; for fluent and raw operations, against the whole `data` object.

The assertion records an `assert.json.shape` operation with the expected type and the operation identifier (`graphql.operation`), and on success records a `graphql.contract.shape` observation carrying the request identifier and the matched property paths. A shape mismatch throws `JsonShapeMismatchException` with every mismatch listed.

A response with no data — `data` missing or `null`, typically errors-only — records a failed `assert.json.shape` (`shape.result = mismatched`) and throws `GraphQLAssertionException("Expected GraphQL data, but the response did not contain data.")` rather than a shape mismatch.

## Reading

```csharp
T? ReadDataAs<T>(JsonSerializerOptions? options = null);   // case-insensitive by default
```

`ReadDataAs` reads the same element `ShouldMatchShape` compares against. It records a `graphql.response.deserialize` operation with `target.type` and rethrows a failure.

## Members

| Member | Type | Notes |
| --- | --- | --- |
| `Errors` | `IReadOnlyList<GraphQLError>` | `GraphQLError(Message, Path, Code, Extensions)`; the code comes from `extensions.code` |
| `HasErrors` / `HasData` | `bool` | |
| `Data` | `JsonElement?` | the full `data` object |
| `SelectedData` | `JsonElement?` | the root field's value for shape-driven operations, else the full `data` object |
| `Extensions` | `JsonElement?` | |
| `HttpStatusCode` | `HttpStatusCode` | |
| `Content` | `string` | the raw body |
| `ElapsedTime` | `TimeSpan` | |
| `RawResponse` | `HttpResponseMessage` | |

## Protocol errors

A body that isn't JSON, or that has neither `data` nor `errors`, throws `GraphQLProtocolException` with the sanitized body in `ResponseContent`. That's distinct from a well-formed response that contains errors — those are for you to assert on.
