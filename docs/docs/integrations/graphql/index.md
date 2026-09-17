---
sidebar_position: 1
title: Overview
---

# GraphQL

`ProtoTest.GraphQL` gives each test a GraphQL client for queries, mutations and subscriptions (WebSocket or SSE), with file uploads, shape assertions and schema coverage. It shares its HTTP plumbing and [authentication model](../rest/authentication.md) with REST.

```bash
dotnet add package ProtoTest.GraphQL
```

## The idea

Most GraphQL test code says the same thing twice: once as a selection set, once as the assertion. ProtoTest lets one anonymous object do both.

```csharp
using var response = await Proto.Context.GraphQL()
    .Mutation("createOrder", new
    {
        input = Gql.Variable("CreateOrderInput!", new CreateOrderRequest("notebook", 2, 12.50m))
    })
    .ExpectAsync(new
    {
        id = JsonValue.GreaterThan(0),
        product = "notebook",
        total = 25m,
        status = "pending"
    });

response.ShouldHaveNoErrors();
```

`ExpectAsync` turns the shape into `{ id product total status }`, sends the mutation with `$input` declared as `CreateOrderInput!`, and asserts the result against the same shape.

## Registering a client

```csharp
builder.AddGraphQL(graphQL => graphQL
    .AddClient("Api", "https://api.example.test/graphql"));
```

`AddClient` has the same three overloads as [REST](../rest/index.md#registering-a-client) — fixed URL, or a resolver that depends on the running test. Configuration works the same way too (`ProtoTest:Clients:{name}:BaseUrl`).

### Reusing another client

When GraphQL is served by an app you already registered — typically an [in-process ASP.NET Core server](../aspnetcore.md) — reuse its `HttpClient` and append the endpoint path:

```csharp
builder
    .AddAspNetCoreServer<Program>("Api")
    .AddGraphQL(graphQL => graphQL
        .AddClientFrom("GraphQL", sourceClientName: "Api", endpointPath: "/graphql"));
```

`endpointPath` defaults to `/graphql`.

### Target options

`AddClient` / `AddClientFrom` return a target builder with GraphQL-specific extensions:

```csharp
graphQL.AddClientFrom("GraphQL", "Api")
    .WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket)   // or Sse
    .WithSchemaCoverage("control-plane.graphql");
```

The transport can also come from configuration — `ProtoTest:Clients:{name}:GraphQL:SubscriptionTransport = "Sse"`. An invalid value throws when the test first calls `GraphQL()`.

### Builder options

```csharp
graphQL.CaptureAttachments(options => options.CaptureExpectedShapes = false);
graphQL.ConfigureResponses(options => options.MaxResponseBodyBytes = 32 * 1024 * 1024);
```

| Section | Options |
| --- | --- |
| `ProtoTest:GraphQL:Attachments` | `CaptureRequestBodies`, `CaptureResponses`, `CaptureExpectedShapes` (all `true`), plus the shared redaction options from [REST attachments](../rest/attachments.md#options) |
| `ProtoTest:GraphQL:Responses` | `MaxResponseBodyBytes` (10 MiB) |

## Selecting a client in a test

```csharp
Proto.Context.GraphQL()            // [GraphQLClient("…")] if present, else "Default"
Proto.Context.GraphQL("GraphQL")   // explicit
```

## Authentication

Use `[GraphQLAuth<T>]` and `[GraphQLClient]` exactly like their REST counterparts. One authenticator class can serve both:

```csharp
[RestClient("Api")]
[GraphQLClient("GraphQL")]
[Auth<SampleUserAuthenticator>]
[GraphQLAuth<SampleUserAuthenticator>]
public sealed class ControlPlaneTests
{
    // ...
}
```

Per request: `.Auth(authenticator)`, `.Auth<T>(args)` and `.WithoutAuth()`. The precedence rules are the ones described under [REST authentication](../rest/authentication.md#precedence).

## Next

- [Queries and mutations](./operations.md) — shape-driven operations, variables, the fluent builder and uploads.
- [Responses](./responses.md) — errors, data and assertions.
- [Subscriptions](./subscriptions.md) — streaming results over WebSocket or SSE.
- [Schema coverage](./coverage.md) — which types, fields and arguments your suite exercised.
