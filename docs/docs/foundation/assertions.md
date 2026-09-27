---
sidebar_position: 6
title: Assertions
description: "One assertion surface across every integration: Should and ShouldNot, chainable members, and shape matching as the chain continuation."
---

# Assertions

Every ProtoTest subject that can be asserted — a REST or GraphQL response, a gRPC reply or failed call, a consumed message, a workbook cell — exposes the same surface: `Should` for the positive form and `ShouldNot` where a negated form is meaningful. Assertion members return the subject, so calls chain:

```csharp
using var response = await Proto.Context.Rest().GetAsync("/api/orders/42");

response.Should.HaveHttpStatus(HttpStatusCode.OK)          // returns the response
    .Should.MatchShape(new { id = 42, status = "pending" });
```

The facade owns the polarity, so one implementation covers both spellings. A negated status or sheet failure reads "Expected … not to …":

```csharp
response.ShouldNot.HaveHttpStatus(HttpStatusCode.NotFound);
sheet.Cell("A1").ShouldNot.BeBlank();
```

The GraphQL error assertions are the exception: they name the opposite state directly, so `response.ShouldNot.HaveErrors()` fails with `Expected no GraphQL errors, but received 1: boom`.

## Where the surface lives

| Subject | Surface |
| --- | --- |
| REST response | `Should`: `HaveHttpStatus`, `HaveContentType`, `HaveHeader`, `HaveCookie`, `HaveRedirectLocation`, `MatchShape`; `ShouldNot`: the same except `MatchShape` |
| GraphQL response | `Should`: `HaveHttpStatus`, `MatchShape`, `HaveNoErrors`, `HaveErrors`, `HaveError(code)`; `ShouldNot`: `HaveHttpStatus` and the error assertions |
| gRPC reply | `ProtoGrpcAssertions.For(reply).Should.MatchShape(shape)`, returning the reply |
| gRPC failed call | `ProtoGrpcAssertions.For(exception).Should.HaveStatus(status)` / `.ShouldNot.HaveStatus(status)` |
| Consumed message | `message.Should.MatchShape(shape)`, returning the message |
| Sheets cell, column, range, table, model column | `Should` / `ShouldNot` (`Be`, `BeText`, `Match`, `ContainRow`, `All`, …), returning the subject |
| Sheets table row | `row.Should.MatchShape(shape)`, returning the row |
| Sheets model row | `row.ShouldMatchShape(shape)`, returning the row (a record is a user type, so C# cannot give it a `Should` extension property) |
| Sheets model | `model.Should.MatchModel()`, returning the model |
| Web element | `element.Should.BeVisibleAsync(…)` and friends; async, so no chaining |

## Shape matching and its subject

`Should.MatchShape(shape)` matches REST and GraphQL responses, gRPC replies, consumed messages and sheet rows with one [shape matcher](./shape-matching.md). It returns the subject, so it continues a chain, and a failure message **starts with the subject** it was made against:

```
GET /api/orders/42 — Shape mismatch failed with 1 error(s):
  • [$.status]: Values did not match. (Expected: "pending", Actual: "cancelled")
```

The subject is the REST request identifier (method and route), the GraphQL operation, the gRPC message type, the messaging destination, or the sheet row's `Sheet!Range`; a model row names the record type. The underlying `JsonShapeMismatchException` — with the full `Mismatches` list — stays reachable as the failure's `InnerException`, and the trace evidence is unchanged.

A table row has no record type, so its shape is keyed by each column's leaf header name and each value is the cell's rendered value; a model row serializes with its record property names.

`MatchShape(shape, exact: true)` is the exhaustive form: a field present in the response that the shape does not mention is a mismatch naming that field, so a response cannot grow a field the test never asserted. A value constraint (`JsonValue.Any()`, `JsonValue.NotNull()`) mentions its whole subtree and never fails an exact match. `PostAsync(…).ExpectAsync(shape)` is the in-call form for REST: it awaits the response and runs the same facade assertion, so the in-call and the after-the-fact spelling share one implementation. The [shape matching page](./shape-matching.md#exact-matching) has the rules.

The HTTP fact assertions name the request too, and read the same on `ShouldNot`:

```csharp
response.Should.HaveContentType("application/json");
response.Should.HaveHeader("X-Correlation", "abc");       // or HaveHeader("X-Correlation") for presence
response.Should.HaveCookie("session");
response.Should.HaveRedirectLocation("/orders/42");
```

## Deliberate exceptions

- **gRPC reaches `Should` through `For(…)`.** C# has no extension properties, so a reply message cannot carry a `Should` property; `ProtoGrpcAssertions.For(reply)` returns the shape facade, and `For(exception)` the status facade. The `ShouldMatchShape` and `ShouldHaveStatus`/`ShouldNotHaveStatus` extension methods remain as obsolete shims.
- **Shape has no negated form.** A negated shape match has no meaning, so there is no `response.ShouldNot.MatchShape(…)`; shape lives on the positive facade. GraphQL's error assertions do honor `ShouldNot`: `ShouldNot.HaveErrors()` is the same check as `Should.HaveNoErrors()`.
- **Web assertions are async.** They poll, so they return `ValueTask` and cannot return the subject; the `Async` suffix marks execution.

The sheet assertions that older code spells as `Verify()` and `ShouldAll(…)` are obsolete shims; new tests use `Should.MatchModel()` and `Should.All(…)`.
