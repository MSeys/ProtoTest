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

## Build an operation

```csharp
using var response = await Proto.Context.GraphQL()
    .Query("FindProducts", query => query
        .Connection("products", products => products
            .Where(filter => filter
                .Contains("name", "note")
                .GreaterThanOrEqual("price", 10m)
                .LessThan("price", 100m))
            .OrderBy(order => order.Ascending("name"))
            .First(10)
            .Nodes("id", "name", "price")
            .PageInfo()))
    .ExecuteAsync();
```

`Connection`, `Where`, `OrderBy`, `Nodes`, and `PageInfo` follow the common Hot
Chocolate and Relay conventions. For another schema, use the underlying generic
`Field`, `Argument`, and `Select` methods. `Request` accepts a raw GraphQL document as
an escape hatch, but fluent operations are the primary API.

Variables stay fluent as well:

```csharp
Proto.Context.GraphQL()
    .Query("Product", query => query
        .Variable("id", GqlType.Id.NonNull())
        .Field("product", field => field
            .Argument("id", Gql.Var("id"))
            .Fields("id", "name")))
    .Variables(new { id = "product-42" });
```

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

Use `ReadDataAs<T>()` for typed deserialization. The raw `HttpResponseMessage`, data,
errors, extensions, content, status, and elapsed time remain available.

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
