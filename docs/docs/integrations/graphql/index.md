---
sidebar_position: 1
title: GraphQL integration testing
sidebar_label: Overview
description: "A per-test GraphQL client for queries, mutations and subscriptions over WebSocket or SSE, with uploads, shape assertions and schema coverage."
---

# GraphQL

## What it adds

`ProtoTest.GraphQL` gives each test a GraphQL client for queries, mutations and subscriptions (WebSocket or SSE), with file uploads, shape assertions and schema coverage. It shares its HTTP plumbing, [authentication model](../rest/authentication.md) and capture options with REST.

Most GraphQL test code says the same thing twice: once as a selection set, once as the assertion. ProtoTest lets one object do both: one anonymous object is the selection set and the assertion.

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

Run it with `dotnet test`. A green run prints the passed test, and the trace lands at `TestResults/prototest-{runId}.prototrace` with a `graphql.operation` entry for the call.

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

Calling `AddGraphQL` twice does not throw. The shared setup runs once. Each callback still adds its clients.

### `AddClient` overloads

| Overload | Base address |
| --- | --- |
| `AddClient(name = null, baseUrl = null, configure = null, endpoint = null)` | the explicit `baseUrl`, else the application's `BaseUrl` joined with `Endpoints:{endpoint}` |
| `AddClient(name, Func<ProtoExecutionContext, Uri> resolver, configure = null)` | resolved per request from the running test |
| `AddClient(name, Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> resolver, configure = null)` | async per-request resolution |

The name may be omitted when the application has one GraphQL client; pass one only to address several targets.

There is **no endpoint default**: `endpoint` names the key under `ProtoTest:Applications:{application}:Endpoints` to append to the application's `BaseUrl`, for host-level and application clients alike. Without it the base URL is used as-is.

An invalid base URL throws `ArgumentException`. A base URL that isn't absolute HTTP(S) fails when the request is sent or the subscription starts.

### Reusing another client

When GraphQL is served by the same [in-process ASP.NET Core server](../aspnetcore.md) as the application, a plain `AddClient` reuses its transport automatically and appends the named endpoint path:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddGraphQL(graphQL => graphQL.AddClient("GraphQL", endpoint: "GraphQL")));
```

Set `ProtoTest:Applications:Api:Endpoints:GraphQL` to change the path. The key has no default: when it is unset, the client uses the application's `BaseUrl` as-is. A configured `BaseUrl` wins and the in-process server stays stopped.

### Target options

`AddClient` returns a target builder with GraphQL-specific extensions:

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

### Options and keys {/* #builder-options */}

| Section | Key | Default |
| --- | --- | --- |
| `ProtoTest:GraphQL:Responses` | `MaxResponseBodyBytes` | `10485760` (10 MiB) |
| | `MaxDiagnosticBodyLength` | `65536` |
| `ProtoTest:GraphQL:Attachments` | `CaptureRequestBodies`, `CaptureResponses`, `CaptureExpectedShapes` | `true` |
| | `RedactSensitiveData`, `MaxDiagnosticBodyLength`, `SensitiveJsonProperties` | shared with REST |
| | `SensitiveHeaders`, `SensitiveQueryParameters` | shared with REST |
| `ProtoTest:Applications:{app}:GraphQL` | `SubscriptionTransport` | `WebSocket`; `Sse` is the other valid value |
| | `Schema` | schema source for `WithSchemaCoverage()` |

Tune them in code or configuration. Configuration binds last, so it wins over code. The transport can come from configuration too; an invalid value throws when the test first calls `GraphQL()`, naming `WebSocket` and `Sse`.

### Context API

```csharp
GraphQLRequestBuilder GraphQL(this ProtoExecutionContext context, string? clientName = null);
```

`Proto.Context.GraphQL(name)` picks the client in this order:

| Step | What it tries |
| --- | --- |
| 1 | the requested name |
| 2 | the client bound by `[Application(…)]` for GraphQL |
| 3 | the application's first registered GraphQL client |
| 4 | `"Default"` |

A requested name first tries its application-qualified form, then the name as given. When exactly one client of the protocol registered that name on another application, the call reaches it. Two applications sharing the name fail, so qualify the call (`App:Client`).

A client with no base address and no owner for its address falls back to the application's in-process transport, rooted at the endpoint the client registered, then `"GraphQL"`, then the requested name, looking up `ProtoTest:Applications:{application}:Endpoints:{name}`. A per-test resolver beats `HttpClient.BaseAddress`. If nothing resolves, the call throws `InvalidOperationException` listing the registered client names.

Creating the builder records a `graphql.builder.create` event with the client, application, resolved source client, whether auth is configured and which resolver supplied the endpoint.

## The tasks

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

### Going further

- **Authentication** - the same `[Auth<T>]`, `.Auth(...)` and `.WithoutAuth()` as [REST](../rest/authentication.md); narrow to GraphQL with `Protocols = ["GraphQL"]`. The [built-in test user](../rest/authentication.md#built-in-test-user) rides the same pipeline.
- **Queries, mutations and uploads** - shape-driven, fluent and raw documents: [Queries and mutations](./operations.md).
- **Subscriptions** - WebSocket or SSE, connection payloads, custom sockets: [Subscriptions](./subscriptions.md).
- **Schema coverage** - point a client at your SDL: [Schema coverage](./coverage.md).
- **Multiple clients** - pass `.GraphQL("Reporting")`, or bind one with `[Application("Api", "GraphQL:Reporting")]`.
- **In-process server** - a client with no URL reuses the application's transport automatically. A configured `BaseUrl` wins and the in-process server stays stopped.

## In the trace and coverage

Queries and mutations record a `graphql.operation` operation (`GraphQL · {type} {name}`) under the protocol-scoped client entity (`client:System.Net.Http.HttpClient:GraphQL:{target}`), with a `graphql.endpoint.resolve` child, the operation type and name, header count, response status and error count; assertions record `assert.http.status`, `assert.graphql.*` and `assert.json.shape` as children.

Deserialization records `graphql.response.deserialize`.

Observations: `graphql.response` for every response, `graphql.failure` when sending fails, and `graphql.contract.shape` when a shape assertion matches. Subscriptions add `graphql.subscription.start|next|complete` events.

`GraphQLCoverageCollector` reports operation-level hits; `GraphQLSchemaCoverageCollector` walks the SDL. See [Schema coverage](./coverage.md) and [Coverage](../../observability/coverage.md).

## Skip

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "GraphQL")]
```

## Limits

- No batching, persisted operations or incremental delivery (`@defer`).
- WebSocket subscriptions speak only `graphql-transport-ws`; the legacy `graphql-ws` protocol is not supported.
- A fluent `.Argument("file", Gql.Upload(...))` is **not** routed through the multipart normalizer; uploads are discovered in shape-driven arguments and in `.Variables(...)`.
- Shape variable type strings are parsed when you create the variable, so an invalid type fails immediately.
- Only one `NextAsync` may be pending at a time.
- The response must be JSON containing `data` or `errors`; anything else throws `GraphQLProtocolException`.
- Upload streams are opened per send; responses are buffered whole in memory; there is no retry/policy layer.

## Next

- [Your first GraphQL suite](./first-suite.md) - the end-to-end page, from an empty project to a subscription.
- [Queries and mutations](./operations.md) - shape-driven operations, variables, the fluent builder and uploads.
- [Responses](./responses.md) - errors, data and assertions.
- [Subscriptions](./subscriptions.md) - streaming results over WebSocket or SSE.
- [Schema coverage](./coverage.md) - which types, fields and arguments your suite exercised.

The full flows live in the demo: [PlatformJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/PlatformJourney.cs) and [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/Setup.cs).
