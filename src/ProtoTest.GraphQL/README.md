# ProtoTest.GraphQL

Runner-independent GraphQL testing with named clients, a fluent operation builder,
GraphQL-aware assertions, observations, and SDL-driven field coverage.

The examples use `JsonValue` from `ProtoTest.Json`; GraphQL and REST intentionally use
the same matcher API.

```csharp
builder.AddApplication("Catalog", app => app.AddGraphQL(graphQL => graphQL
    .CaptureAttachments()
    .AddClient("Catalog")
    .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "catalog.graphql"))));
```

As with REST, the endpoint comes from the application's
`ProtoTest:Applications:Catalog:BaseUrl`, joined with `Endpoints:GraphQL` when configured. For an
endpoint created per test, pass a `Func<ProtoExecutionContext, Uri>` to `AddClient`. Response
buffering is bounded by `ConfigureResponses(options => options.MaxResponseBodyBytes = ...)`.

When another integration already owns the transport, such as an in-process
`ProtoTest.AspNetCore` server, reuse that client without opening a network connection:

```csharp
builder.AddApplication("SampleApp", app => app
    .AddAspNetCoreServer<Program>()
    .AddGraphQL(graphQL => graphQL.AddClient("GraphQL")));   // reuses the server; set Endpoints:GraphQL for the path
```

```csharp
[Application("Catalog")]
public async Task FindsProducts()
{
    using var response = await Proto.Context.GraphQL()
        .Query("products", new
        {
            first = 10,
            where = new { name = new { contains = "note" } },
            order = new[] { new { name = Gql.Enum("ASC") } }
        })
        .ExpectAsync(new
        {
            nodes = new[]
            {
                new
                {
                    id = JsonValue.NotNull(),
                    name = JsonValue.StringContaining("note"),
                    price = JsonValue.LessThan(100m)
                }
            },
            pageInfo = new { hasNextPage = false }
        });

    response.ShouldHaveNoErrors();
}
```

Anonymous objects or test-owned contract types drive the selection, while arguments
remain ordinary objects. Use `Gql.Variable("InputType!", value)` for safe typed variables.
Direct file uploads stay compact:

```csharp
using var response = await Proto.Context.GraphQL()
    .Mutation("uploadDocument", new
    {
        file = Gql.Upload(bytes, "example.txt", "text/plain")
    })
    .ExpectAsync(new { fileName = "example.txt", length = JsonValue.GreaterThan(0) });
```

ProtoTest creates the multipart map and preflight header automatically. Uploads also
work inside explicitly typed variables and with raw documents. Use the detailed
`Field`, `Argument`, connection and variable builders for advanced operations.
`Request` accepts a raw GraphQL document as the final escape hatch.

Subscriptions use `graphql-transport-ws` by default and return a separately owned stream:

```csharp
await using var subscription = await Proto.Context.GraphQL()
    .Subscription("orderCreated")
    .Select(new { id = Gql.Field, status = Gql.Field })
    .SubscribeAsync();

using var message = await subscription.ExpectNextAsync(expected, cancellationToken);
message.ShouldHaveNoErrors();
```

`GraphQLSubscription` also supports `ExpectNextAsync` and `await foreach`. Each event is
a normal `GraphQLResponse` and contributes its own observations, shape assertions,
attachments, and schema coverage. Select
`GraphQLSubscriptionTransport.Sse` with `WithSubscriptionTransport(...)` for HTTP-based
streams. The appsettings equivalent is
`ProtoTest:Applications:{name}:GraphQL:SubscriptionTransport`. Custom in-process hosts can
provide `IGraphQLWebSocketFactory`, and `ConnectionPayload(...)` configures optional
`connection_init` metadata. Batching, persisted operations, and incremental responses
remain separate future features.

Schema coverage parses SDL and reports every object/interface field, including fields
that were not selected. Aliases are resolved to their schema field, and fragments and
inline fragments participate in coverage. Field arguments and nested input properties
are tracked for both inline values and variables.
The schema source accepts inline SDL, a file, or a URL. The parameterless
`WithSchemaCoverage()` reads `ProtoTest:Applications:{name}:GraphQL:Schema`.

Shape matching is powered by `ProtoTest.Json`, the protocol-independent matcher
shared with `ProtoTest.Rest`.
