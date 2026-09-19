---
sidebar_position: 1
title: Overview
description: "A per-test GraphQL client for queries, mutations and subscriptions over WebSocket or SSE, with uploads, shape assertions and schema coverage."
---

# GraphQL

`ProtoTest.GraphQL` gives each test a GraphQL client for queries, mutations and subscriptions (WebSocket or SSE), with file uploads, shape assertions and schema coverage. It shares its HTTP plumbing, [authentication model](../rest/authentication.md) and capture options with REST.

```bash
dotnet add package ProtoTest.GraphQL --prerelease
```

ProtoTest targets **.NET 8, 9 and 10**. The template defaults to `net10.0` unless you pass `-f net8.0` (or `net9.0`) to `dotnet new`.

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

## Registering

Register GraphQL on the host, or under an application so its clients share the application's base URL:

```csharp
builder.AddGraphQL(graphQL =>
    graphQL.AddClient("Api", "https://api.example.test/graphql"));

builder.AddApplication("Api", app => app.AddGraphQL(graphQL =>
    graphQL.AddClient("GraphQL")));       // BaseUrl + Endpoints:GraphQL
```

Repeated registration never errors: the lifecycle hook, WebSocket factory and keyed options register once, while every `AddGraphQL` callback still runs and composes more clients.

### `AddClient` overloads

| Overload | Base address |
| --- | --- |
| `AddClient(name = "Default", baseUrl = null, configure = null, endpoint = "GraphQL")` | the explicit `baseUrl`, else the application's `BaseUrl` joined with `Endpoints:{endpoint}` |
| `AddClient(name, Func<ProtoExecutionContext, Uri> resolver, configure = null)` | resolved per request from the running test |
| `AddClient(name, Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> resolver, configure = null)` | async per-request resolution |
| `AddClientFrom(name, sourceClientName, endpointPath = "/graphql")` | another integration's registered client, rooted at `endpointPath` |

The **endpoint default rule** for GraphQL is `"GraphQL"`: it names the key under `ProtoTest:Applications:{application}:Endpoints` to append to the application's `BaseUrl` and applies whether the client is registered on the host or under an application. `AddClientFrom`'s `endpointPath` is non-nullable and defaults to `/graphql` — REST's `basePath` equivalent is nullable.

An invalid base URL throws `ArgumentException`. A base URL that isn't absolute HTTP(S) fails when the request is sent or the subscription starts.

### Reusing another client

When GraphQL is served by the same [in-process ASP.NET Core server](../aspnetcore.md) as the application, a plain `AddClient` reuses its transport automatically and appends the `GraphQL` endpoint path:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddGraphQL(graphQL => graphQL.AddClient("GraphQL")));
```

Set `ProtoTest:Applications:Api:Endpoints:GraphQL` to change the path. The key has no default: when it is unset, the client uses the application's `BaseUrl` as-is. If you must reuse a *differently named* client, `AddClientFrom(name, sourceClientName, endpointPath)` does that explicitly.

### Target options

`AddClient` / `AddClientFrom` return a target builder with GraphQL-specific extensions:

| Extension | Effect |
| --- | --- |
| `WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket \| Sse)` | per-target subscription transport |
| `WithSchemaCoverage()` | schema coverage reading `ProtoTest:Applications:{app}:GraphQL:Schema` |
| `WithSchemaCoverage(string schemaSource)` | schema coverage from a file path, inline SDL or URL |

```csharp
graphQL.AddClient("GraphQL")
    .WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket)
    .WithSchemaCoverage("northstar.graphql");
```

## Options and keys {/* #builder-options */}

| Section | Key | Default |
| --- | --- | --- |
| `ProtoTest:GraphQL:Responses` | `MaxResponseBodyBytes` | `10485760` (10 MiB) |
| | `MaxDiagnosticBodyLength` | `65536` |
| `ProtoTest:GraphQL:Attachments` | `CaptureRequestBodies`, `CaptureResponses`, `CaptureExpectedShapes` | `true` |
| | `RedactSensitiveData`, `MaxDiagnosticBodyLength`, `SensitiveJsonProperties` | shared with REST |
| | `SensitiveHeaders`, `SensitiveQueryParameters` | shared with REST |
| `ProtoTest:Applications:{app}:GraphQL` | `SubscriptionTransport` | `WebSocket`; `Sse` is the other valid value |
| | `Schema` | schema source for `WithSchemaCoverage()` |

Tune the options in code with `ConfigureResponses(...)` and `CaptureAttachments(...)`; code callbacks compose in registration order and the known section binds over the result, so configuration wins over code. The transport can come from configuration too — an invalid value throws when the test first calls `GraphQL()`, naming `WebSocket` and `Sse`.

## Context API

```csharp
GraphQLRequestBuilder GraphQL(this ProtoExecutionContext context, string? clientName = null);
```

`Proto.Context.GraphQL(name)` resolves the client in this order: the requested name, the client bound by `[Application(…)]` for GraphQL, the application's first registered GraphQL client, then `"Default"`. A client with no base address and no owner for its address falls back to the application's in-process transport, rooted at the endpoint the client registered, then `"GraphQL"`, then the requested name — looking up `ProtoTest:Applications:{application}:Endpoints:{name}`. A per-test resolver beats `HttpClient.BaseAddress`; if nothing resolves, the call throws `InvalidOperationException`.

Creating the builder records a `graphql.builder.create` event with the client, application, resolved source client, whether auth is configured and which resolver supplied the endpoint.

## Quick start

```csharp
[Application("Api")]
public sealed class ViewerTests
{
    [ProtoTest]
    public async Task Counts_workspaces()
    {
        using var response = await Proto.Context.GraphQL()
            .Query("controlPlane")
            .ExpectAsync(new { workspaceCount = JsonValue.GreaterThan(0) });

        response.ShouldHaveNoErrors();
    }
}
```

## Going further

- **Authentication** — the same `[Auth<T>]`, `.Auth(...)` and `.WithoutAuth()` as [REST](../rest/authentication.md); narrow to GraphQL with `Protocols = ["GraphQL"]`.
- **Queries, mutations and uploads** — shape-driven, fluent and raw documents: [Queries and mutations](./operations.md).
- **Subscriptions** — WebSocket or SSE, connection payloads, custom sockets: [Subscriptions](./subscriptions.md).
- **Schema coverage** — point a client at your SDL: [Schema coverage](./coverage.md).
- **Multiple clients** — pass `.GraphQL("Reporting")`, or bind one with `[Application("Api", "GraphQL:Reporting")]`.
- **In-process server** — a client with no URL reuses the application's transport; `AddClientFrom` points at another client explicitly.

## Tracing and coverage

Queries and mutations record a `graphql.operation` operation (`GraphQL · {type} {name}`) under `client:HttpClient:{target}`, with a `graphql.endpoint.resolve` child, the operation type and name, header count, response status and error count; assertions record `assert.http.status`, `assert.graphql.*` and `assert.json.shape` as children. Deserialization records `graphql.response.deserialize`. Observations: `graphql.response` for every response, `graphql.failure` when sending fails, and `graphql.contract.shape` when a shape assertion matches. Subscriptions add `graphql.subscription.start|next|complete` events.

`GraphQLCoverageCollector` reports operation-level hits; `GraphQLSchemaCoverageCollector` walks the SDL — see [Schema coverage](./coverage.md) and [Coverage](../../observability/coverage.md).

## Skip

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "GraphQL")]
```

## Limits

- No batching, persisted operations or incremental delivery (`@defer`).
- WebSocket subscriptions speak only `graphql-transport-ws` — no legacy `graphql-ws` protocol.
- A fluent `.Argument("file", Gql.Upload(...))` is **not** routed through the multipart normalizer; uploads are discovered in shape-driven arguments and in `.Variables(...)`.
- Shape variable type strings are parsed when you create the variable, so an invalid type fails immediately.
- Only one `NextAsync` may be pending at a time.
- The response must be JSON containing `data` or `errors`; anything else throws `GraphQLProtocolException`.
- Upload streams are opened per send; responses are buffered whole in memory; there is no retry/policy layer.

## Next

- [Queries and mutations](./operations.md) — shape-driven operations, variables, the fluent builder and uploads.
- [Responses](./responses.md) — errors, data and assertions.
- [Subscriptions](./subscriptions.md) — streaming results over WebSocket or SSE.
- [Schema coverage](./coverage.md) — which types, fields and arguments your suite exercised.

The full flows live in the demo: [PlatformJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/PlatformJourney.cs) and [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs).
