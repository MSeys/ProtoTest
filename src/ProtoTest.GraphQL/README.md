# ProtoTest.GraphQL

Queries, mutations and subscriptions over named clients, with a fluent and a shape-driven builder, GraphQL-aware assertions and SDL-driven schema coverage.

```bash
dotnet add package ProtoTest.GraphQL
```

## Quick start

```csharp
builder.AddApplication("Api", app => app.AddGraphQL(graphQL => graphQL
    .CaptureAttachments()
    .AddClient("GraphQL")));

[Application("Api")]
public sealed class CatalogTests
{
    [ProtoTest]
    public async Task FindsProducts()
    {
        using var response = await Proto.Context.GraphQL()
            .Query("products", new { first = 10 })
            .ExpectAsync(new
            {
                nodes = new[]
                {
                    new { id = JsonValue.NotNull(), name = JsonValue.StringContaining("note") }
                },
                pageInfo = new { hasNextPage = false }
            });

        response.ShouldHaveNoErrors();
    }
}
```

## What it adds

- **Operations** — `Proto.Context.GraphQL(name?)` with fluent `Query`/`Mutation`/`Subscription(name, configure)`, shape-driven `Query(rootField, arguments)` + `ExpectAsync(shape)`, and `Request(document)` as the raw escape hatch.
- **Assertions** — `ShouldHaveNoErrors()`, `ShouldHaveErrors()`, `ShouldHaveError(code)`, `Should.HaveHttpStatus(...)`, `ShouldMatchShape(...)` and `ReadDataAs<T>()`.
- **Subscriptions** — `SubscribeAsync()` over `graphql-transport-ws` WebSocket or SSE, selected per target with `WithSubscriptionTransport(...)` or `ProtoTest:Applications:{app}:GraphQL:SubscriptionTransport`; each event is a normal `GraphQLResponse`.
- **Uploads** — `Gql.Upload(...)` in shape arguments and variables builds the multipart request and preflight header automatically.
- **Coverage** — `GraphQLCoverageCollector` counts operations; `WithSchemaCoverage()` walks the SDL (inline, file or URL) and reports types, fields, arguments and input fields.
- **Tracing** — `graphql.operation` with document and variables, `graphql.response` observations, redacted request/response attachments and subscription events.

## Configuration

Under `ProtoTest:GraphQL:Responses` and `ProtoTest:GraphQL:Attachments`; the option types are shared with REST, each protocol owns a keyed instance.

| Key | Type | Default |
| --- | --- | --- |
| `MaxResponseBodyBytes` | `int` | `10485760` (10 MiB) |
| `MaxDiagnosticBodyLength` | `int` | `65536` |
| `CaptureRequestBodies` | `bool` | `true` |
| `CaptureResponses` | `bool` | `true` |
| `CaptureExpectedShapes` | `bool` | `true` |
| `RedactSensitiveData` | `bool` | `true` |
| `SensitiveHeaders` | `List<string>` | `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` |
| `SensitiveQueryParameters` | `List<string>` | `access_token`, `refresh_token`, `token`, `apiKey`, `api_key`, `key` |
| `SensitiveJsonProperties` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key` |

There is no batching, persisted operations or incremental `@defer` support; subscriptions speak only `graphql-transport-ws`, and a fluent `.Argument("file", Gql.Upload(...))` is not routed through multipart normalization.

## Learn more

- [GraphQL guide](https://prototest.dev/docs/integrations/graphql/)
- [PlatformJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/PlatformJourney.cs)
