# GraphQL integration

`ProtoTest.GraphQL` builds queries and mutations fluently, executes them through named
HTTP clients, exposes the complete GraphQL response envelope, and records operation,
shape, and schema-field coverage observations.

## Configure a client

```csharp
builder.AddGraphQL(graphQL => graphQL
    .AddClient("Catalog", "https://localhost:5001/graphql")
    .WithSchemaCoverage("catalog.graphql")
    .WithCollector<GraphQLCoverageCollector>());
```

The endpoint can also be configured as
`ProtoTest:Clients:Catalog:BaseUrl`, exactly like REST. An explicit URL wins over
configuration. A per-test endpoint is also supported:

```csharp
graphQL.AddClient("Catalog", context =>
    context.Service<TestApplication>().GraphQlEndpoint);
```

Use `[GraphQLClient("Catalog")]` on a test class
or method, or select it explicitly with `Proto.Context.GraphQL("Catalog")`.

Enable request, response, and expected-shape attachments with
`graphQL.CaptureAttachments()`. Sensitive JSON properties are redacted. Bound response
memory with `graphQL.ConfigureResponses(options => options.MaxResponseBodyBytes = ...)`;
both option groups can be overridden through `ProtoTest:GraphQL` configuration.

Schema coverage can use inline SDL, a local file, or an absolute URL through
`WithSchemaCoverage(source)`. Alternatively, call `WithSchemaCoverage()` and configure
the single canonical key `ProtoTest:Clients:Catalog:GraphQL:Schema`. A configured schema
URL may be relative to the client `BaseUrl`.

## Shape-first operations

For most tests, provide the root field, its arguments, and the expected result. ProtoTest
derives the GraphQL selection set from the expected anonymous shape:

```csharp
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
                price = JsonValue.Between(10m, 100m)
            }
        },
        pageInfo = new { hasNextPage = false }
    });

response.ShouldHaveNoErrors();
```

`ExpectAsync` combines selection, execution, and shape matching. Keep execution and
assertion separate when errors or extensions are the subject of the test:

```csharp
using var response = await Proto.Context.GraphQL()
    .Mutation("createOrder", new
    {
        input = Gql.Variable("CreateOrderInput!", command)
    })
    .Select(new { id = Gql.Field, status = Gql.Field })
    .ExecuteAsync();

response.ShouldHaveErrors().ShouldHaveError("INVALID_ORDER");
```

`Select<TContract>()` derives the same selection from a test-owned contract type, so
tests do not need to reference application DTOs. `ReadDataAs<T>()` deserializes the
selected root-field value for shape-driven operations.

```csharp
private sealed record ProductResult(string Id, string Name, decimal Price);

using var response = await Proto.Context.GraphQL()
    .Query("product", new { id = "product-42" })
    .Select<ProductResult>()
    .ExecuteAsync();

var product = response.ReadDataAs<ProductResult>();
```

## Variables and uploads

`Gql.Variable` keeps complex values out of the generated document while retaining an
explicit GraphQL type. The value can be an anonymous object or a test-owned contract:

```csharp
using var response = await Proto.Context.GraphQL()
    .Mutation("createOrder", new
    {
        input = Gql.Variable("CreateOrderInput!", new
        {
            product = "notebook",
            quantity = 2,
            unitPrice = 12.50m
        })
    })
    .ExpectAsync(new { id = JsonValue.NotNull(), status = "pending" });
```

A direct upload is even shorter. ProtoTest generates the `Upload!` variable, multipart
map, and GraphQL preflight header automatically:

```csharp
using var response = await Proto.Context.GraphQL()
    .Mutation("uploadDocument", new
    {
        file = Gql.Upload(bytes, "example.txt", "text/plain")
    })
    .ExpectAsync(new
    {
        fileName = "example.txt",
        length = JsonValue.GreaterThan(0)
    });
```

Use the `Func<Stream>` overload for large or lazily opened files. Uploads nested inside
input objects or lists remain possible by wrapping that complete value in
`Gql.Variable(...)`. Raw documents can use `.Variables(...)` with `Gql.Upload(...)` too.

## Advanced operations

The detailed fluent builder remains available for aliases, multiple root fields,
explicit variables, fragments, and other advanced documents:

```csharp
Proto.Context.GraphQL()
    .Query("Product", query => query
        .Variable("id", GqlType.Id.NonNull())
        .Field("product", field => field
            .Argument("id", Gql.Var("id"))
            .Fields("id", "name")))
    .Variables(new { id = "product-42" });
```

`Connection`, `Where`, `OrderBy`, `Nodes`, and `PageInfo` follow common Hot Chocolate
and Relay conventions. `Request` accepts a raw GraphQL document as the final escape
hatch.

## Subscriptions

Subscriptions use the modern `graphql-transport-ws` WebSocket protocol by default.
Opening the stream is deliberately separate from reading its results:

```csharp
var expected = new
{
    id = JsonValue.GreaterThan(0),
    status = "pending"
};

await using var subscription = await Proto.Context.GraphQL()
    .Subscription("orderCreated")
    .Select(expected)
    .SubscribeAsync();

// Trigger the event through the system under test.

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using var message = await subscription.ExpectNextAsync(expected, timeout.Token);
message.ShouldHaveNoErrors();
```

Use `NextAsync` when each event has different expectations, or consume the subscription
with `await foreach` for a continuous stream. Disposal closes the transport stream, and every
received result participates independently in assertions, attachments, observations,
and SDL coverage. Authentication, headers, variables, raw documents, and the detailed
selection builder work the same way as for queries and mutations.

Select SSE explicitly when the server or test host exposes subscriptions over HTTP:

```csharp
graphQL.AddClient("Catalog", "https://localhost:5001/graphql")
    .WithSubscriptionTransport(GraphQLSubscriptionTransport.Sse);
```

The same choice is available through the canonical configuration key
`ProtoTest:Clients:Catalog:GraphQL:SubscriptionTransport`, with `WebSocket` and `Sse` as
the only accepted values. `IGraphQLWebSocketFactory` is the extension point for custom
hosts such as ASP.NET Core `TestServer`; normal network clients use `ClientWebSocket`.
Use `ConnectionPayload(...)` when a server expects authentication or other metadata in
the `connection_init` payload instead of handshake headers.

HTTP batching, persisted operations, and incremental `@defer`/`@stream` responses
remain separate future features.

## Assert the response

```csharp
response
    .ShouldHaveNoErrors()
    .ShouldMatchData(new
    {
        products = new
        {
            nodes = new[]
            {
                new
                {
                    id = JsonValue.NotNull(),
                    name = JsonValue.StringContaining("note"),
                    price = JsonValue.Between(10m, 100m)
                }
            }
        }
    });
```

GraphQL execution errors remain inspectable even when the HTTP status is successful:

```csharp
response.ShouldHaveErrors().ShouldHaveError("PRODUCT_NOT_FOUND");
```

Use `ReadDataAs<T>()` for typed deserialization. The raw `HttpResponseMessage`, full
`Data`, shape-driven `SelectedData`, errors, extensions, content, status, and elapsed
time remain available.

The shape engine and `JsonValue` constraints live in `ProtoTest.Json` and are shared
directly with REST.

## Authentication

Apply authentication through an attribute or one request:

```csharp
[GraphQLAuth<GraphQLBearerTokenAuthenticator>("token")]

Proto.Context.GraphQL()
    .Auth<GraphQLBearerTokenAuthenticator>("token");
```

Implement `IGraphQLAuthenticator` for context-aware authentication. `WithoutAuth()`
disables inherited authentication for one request.

## Coverage

`GraphQLCoverageCollector` counts executed named operations. `WithSchemaCoverage`
parses SDL with Hot Chocolate and creates report entries for every object/interface
field, field argument, input type, and input field, including uncovered entries.
Selections through aliases, inline fragments, and named fragments are mapped back to
their schema identifiers. Literal and variable input objects are followed recursively,
so supplied filter, paging, and mutation input properties are counted against their SDL
input-field definitions.

The generated items use the existing ProtoTest report contracts and therefore appear
automatically in JSON and HTML report sinks.
