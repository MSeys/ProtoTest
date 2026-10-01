---
sidebar_position: 1
title: Test GraphQL APIs in .NET
sidebar_label: Overview
description: "A per-test GraphQL client for queries, mutations and subscriptions over WebSocket or SSE, with uploads, shape assertions and schema coverage."
---

# Test GraphQL APIs in .NET

`ProtoTest.GraphQL` gives each test a GraphQL client for queries, mutations and subscriptions (WebSocket or SSE), with file uploads, shape assertions and schema coverage.

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

        response.Should.HaveNoErrors();
    }
}
```

Run it with `dotnet test`. A green run prints `Passed Counts_workspaces`, and the trace lands at `TestResults/prototest-{runId}.prototrace` with a `graphql.operation` entry for the call.

## What it adds

Each test gets a GraphQL client with shape assertions, the shared [authentication model](../rest/authentication.md) and capture options shared with REST.

Most GraphQL test code says the same thing twice: once as a selection set, once as the assertion. In ProtoTest one anonymous object is both. The mutation below shows that shape-driven style:

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

response.Should.HaveNoErrors();
```

`ExpectAsync` turns the shape into `{ id product total status }`, sends the mutation with `$input` declared as `CreateOrderInput!`, and asserts the result against the same shape.

## Install

```bash
dotnet add package ProtoTest.GraphQL
```

See [Installation](../../getting-started/installation.md) for the supported .NET versions.

## Compose

Register GraphQL on the host, or under an application so its clients share the application's base URL:

```csharp
builder.AddGraphQL(graphQL =>
    graphQL.AddClient("Api", "https://api.example.test/graphql"));

builder.AddApplication("Api", app => app.AddGraphQL(graphQL =>
    graphQL.AddClient("GraphQL", endpoint: "GraphQL")));       // BaseUrl + Endpoints:GraphQL
```

Calling `AddGraphQL` twice does not throw. The shared setup runs once, and each callback still adds its clients. Omit the name when the application has one GraphQL client. Pass one only to address several targets.

When GraphQL is served by the same [in-process ASP.NET Core server](../aspnetcore.md) as the application, a plain `AddClient` reuses its transport automatically. A configured `BaseUrl` wins and the in-process server stays stopped.

## The tasks

The `Counts_workspaces` test above is the whole pattern: pick the client, name the field, assert the shape. The pages below cover each step.

### Going further

- **Authentication** - the same `[Auth<T>]`, `.Auth(...)` and `.WithoutAuth()` as [REST](../rest/authentication.md). Narrow to GraphQL with `Protocols = ["GraphQL"]`. The [built-in test user](../rest/authentication.md#built-in-test-user) rides the same pipeline.
- **Queries, mutations and uploads** - shape-driven, fluent and raw documents: [Queries and mutations](./operations.md).
- **Subscriptions** - WebSocket or SSE, connection payloads, custom sockets: [Subscriptions](./subscriptions.md).
- **Schema coverage** - point a client at your SDL: [Schema coverage](./coverage.md).
- **Multiple clients** - pass `.GraphQL("Reporting")`, or bind one with `[Application("Api", "GraphQL:Reporting")]`.
- **In-process server** - a client with no URL reuses the application's transport automatically. A configured `BaseUrl` wins and the in-process server stays stopped.

### Reference: resolution and options

```csharp
graphQL.AddClient("GraphQL", endpoint: "GraphQL");
graphQL.AddClient("GraphQL")
    .WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket)
    .WithSchemaCoverage("northstar.graphql");
```

| Overload | Base address |
| --- | --- |
| `AddClient(name = null, baseUrl = null, configure = null, endpoint = null)` | the explicit `baseUrl`, else the application's `BaseUrl` joined with `Endpoints:{endpoint}` |
| `AddClient(name, Func<ProtoExecutionContext, Uri> resolver, configure = null)` | resolved per request from the running test |
| `AddClient(name, Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> resolver, configure = null)` | async per-request resolution |

There is **no endpoint default**. `endpoint` names the key under `ProtoTest:Applications:{application}:Endpoints` to append to the application's `BaseUrl`, for host-level and application clients alike. Without it the base URL is used as-is. An invalid base URL throws `ArgumentException`. A base URL that is not absolute HTTP(S) fails when the request is sent or the subscription starts.

| Extension | Effect |
| --- | --- |
| `WithSubscriptionTransport(GraphQLSubscriptionTransport.WebSocket \| Sse)` | per-target subscription transport |
| `WithSchemaCoverage()` | schema coverage reading `ProtoTest:Applications:{app}:GraphQL:Schema` |
| `WithSchemaCoverage(string schemaSource)` | schema coverage from a file path, inline SDL or URL |

```csharp
GraphQLRequestBuilder GraphQL(this ProtoExecutionContext context, string? clientName = null);
```

`Proto.Context.GraphQL(name)` picks the client in this order:

1. the requested name
2. the client bound by `[Application(…)]` for GraphQL
3. the application's first registered GraphQL client
4. `"Default"`.

 A requested name first tries its application-qualified form, then the name as given. When exactly one client of the protocol registered that name on another application, the call reaches it. Two applications sharing the name fail, so qualify the call (`App:Client`).

A client with no base address and no owner for its address falls back to the application's in-process transport. That transport is rooted at the endpoint the client registered, else `"GraphQL"`, else the requested name, looked up as `ProtoTest:Applications:{application}:Endpoints:{name}`. A per-test resolver beats `HttpClient.BaseAddress`. If nothing resolves, the call throws `InvalidOperationException` listing the registered client names.

Creating the builder records a `graphql.builder.create` event. It holds the client, application, resolved source client, whether auth is configured and which resolver supplied the endpoint.

| Section | Key | Default |
| --- | --- | --- |
| `ProtoTest:GraphQL:Responses` | `MaxResponseBodyBytes` | `10485760` (10 MiB) |
| | `MaxDiagnosticBodyLength` | `65536` |
| `ProtoTest:GraphQL:Attachments` | `CaptureRequestBodies`, `CaptureResponses`, `CaptureExpectedShapes` | `true` |
| | `RedactSensitiveData`, `MaxDiagnosticBodyLength`, `SensitiveJsonProperties` | shared with REST |
| | `SensitiveHeaders`, `SensitiveQueryParameters` | shared with REST |
| `ProtoTest:Applications:{app}:GraphQL` | `SubscriptionTransport` | `WebSocket`; `Sse` is the other valid value |
| | `Schema` | schema source for `WithSchemaCoverage()` |

Tune them in code or configuration. Configuration binds last, so it wins over code. The transport can come from configuration too. An invalid value throws when the test first calls `GraphQL()`, naming `WebSocket` and `Sse`.

## In the trace and coverage

Queries and mutations record a `graphql.operation` operation (`GraphQL · {type} {name}`) under the protocol-scoped client entity (`client:System.Net.Http.HttpClient:GraphQL:{target}`). It has a `graphql.endpoint.resolve` child and carries the operation type and name, header count, response status and error count. Assertions record `assert.http.status`, `assert.graphql.*` and `assert.json.shape` as children.

Deserialization records `graphql.response.deserialize`.

The observations are `graphql.response` for every response, `graphql.failure` when sending fails, and `graphql.contract.shape` when a shape assertion matches. Subscriptions add `graphql.subscription.start|next|complete` events.

`GraphQLCoverageCollector` reports operation-level hits, and `GraphQLSchemaCoverageCollector` walks the SDL. See [Schema coverage](./coverage.md) and [Coverage](../../observability/coverage.md).

## Skip

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "GraphQL")]
```

## Limits

- No batching, persisted operations or incremental delivery (`@defer`).
- WebSocket subscriptions speak only `graphql-transport-ws`. The legacy `graphql-ws` protocol is not supported.
- A fluent `.Argument("file", Gql.Upload(...))` fails when the document is built. Uploads travel through shape-driven arguments or `.Variables(...)`, which send them as multipart variables.
- Shape variable type strings are parsed when you create the variable, so an invalid type fails immediately.
- Only one `NextAsync` may be pending at a time.
- The response must be JSON containing `data` or `errors`; anything else throws `GraphQLProtocolException`.
- Upload streams are opened per send.
- Responses are buffered whole in memory.
- There is no retry or policy layer.

## Next

- [Your first GraphQL suite](./first-suite.md) - the end-to-end page, from an empty project to a subscription.
- [Queries and mutations](./operations.md) - shape-driven operations, variables, the fluent builder and uploads.
- [Responses](./responses.md) - errors, data and assertions.
- [Subscriptions](./subscriptions.md) - streaming results over WebSocket or SSE.
- [Schema coverage](./coverage.md) - which types, fields and arguments your suite exercised.

The full flows live in the demo: [PlatformJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/PlatformJourney.cs) and [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/Setup.cs).
