---
sidebar_position: 3
title: Responses
---

# Responses

`ExecuteAsync()` and `ExpectAsync()` return a `GraphQLResponse`. It's `IDisposable` — use `using var`.

GraphQL servers usually answer `200 OK` even when an operation fails, so the interesting part is the `errors` array, not the status code.

## Assertions

All of them return the response, so they chain.

```csharp
GraphQLResponse ShouldHaveNoErrors();
GraphQLResponse ShouldHaveErrors();
GraphQLResponse ShouldHaveError(string code);                        // matches extensions.code, case-insensitive
GraphQLResponse ShouldHaveHttpStatus(HttpStatusCode expected);
GraphQLResponse ShouldMatchData(object expectedShape, JsonSerializerOptions? options = null);
```

```csharp
using var response = await Proto.Context.GraphQL()
    .WithoutAuth()
    .Query("me")
    .Select(new { id = Gql.Field })
    .ExecuteAsync();

response.ShouldHaveErrors().ShouldHaveError("UNAUTHORIZED");
```

`ShouldMatchData` uses the same rules as REST's `ShouldMatchShape` — partial objects, exact arrays, `JsonValue` constraints — see [Shape matching](../../advanced/json-shapes.md). For shape-driven operations it compares against the root field's value; for fluent and raw operations, against the whole `data` object.

Assertion failures throw `GraphQLAssertionException`; shape failures throw `JsonShapeMismatchException` with every mismatch listed.

## Reading

```csharp
T? ReadDataAs<T>(JsonSerializerOptions? options = null);   // case-insensitive by default
```

`ReadDataAs` reads the same element `ShouldMatchData` compares against.

## Members

| Member | Type | Notes |
| --- | --- | --- |
| `Errors` | `IReadOnlyList<GraphQLError>` | `GraphQLError(Message, Path, Code, Extensions)` |
| `HasErrors` / `HasData` | `bool` | |
| `Data` | `JsonElement?` | the full `data` object |
| `SelectedData` | `JsonElement?` | the root field's value for shape-driven operations |
| `Extensions` | `JsonElement?` | |
| `HttpStatusCode` | `HttpStatusCode` | |
| `Content` | `string` | the raw body |
| `ElapsedTime` | `TimeSpan` | |
| `RawResponse` | `HttpResponseMessage` | |

## Protocol errors

A body that isn't JSON, or that has neither `data` nor `errors`, throws `GraphQLProtocolException` with the body in `ResponseContent`. That's distinct from a well-formed response that contains errors — those are for you to assert on.
