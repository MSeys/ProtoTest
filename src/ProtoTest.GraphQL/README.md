# ProtoTest.GraphQL

Runner-independent GraphQL testing with named clients, a fluent operation builder,
GraphQL-aware assertions, observations, and SDL-driven field coverage.

The examples use `JsonValue` from `ProtoTest.Json`; GraphQL and REST intentionally use
the same matcher API.

```csharp
builder.AddGraphQL(graphQL => graphQL
    .CaptureAttachments()
    .AddClient("Catalog", "https://api.example.test/graphql")
    .WithSchemaCoverage("catalog.graphql")
    .WithCollector<GraphQLCoverageCollector>());
```

As with REST, the URL can be supplied through
`ProtoTest:Clients:Catalog:BaseUrl`. For an endpoint created per test, pass a
`Func<ProtoExecutionContext, Uri>` to `AddClient`. Response buffering is bounded by
`ConfigureResponses(options => options.MaxResponseBodyBytes = ...)`.

When another integration already owns the transport, such as an in-process
`ProtoTest.AspNetCore` server, reuse that client without opening a network connection:

```csharp
builder
    .AddAspNetCoreServer<Program>("SampleApp")
    .AddGraphQL(graphQL => graphQL
        .AddClientFrom("SampleAppGraphQL", "SampleApp", "/graphql"));
```

```csharp
[GraphQLClient("Catalog")]
public async Task FindsProducts()
{
    using var response = await Proto.Context.GraphQL()
        .Query("FindProducts", query => query
            .Connection("products", products => products
                .Where(filter => filter
                    .Contains("name", "note")
                    .LessThan("price", 100m))
                .OrderBy(order => order.Ascending("name"))
                .First(10)
                .Nodes("id", "name", "price")
                .PageInfo("hasNextPage", "endCursor")))
        .ExecuteAsync();

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
                        price = JsonValue.LessThan(100m)
                    }
                }
            }
        });
}
```

The connection, filtering, and ordering helpers follow Hot Chocolate conventions.
Use `Field`, `Argument`, and `Select` for arbitrary GraphQL schemas. `Request` accepts
a raw GraphQL document as an escape hatch.

Schema coverage parses SDL and reports every object/interface field, including fields
that were not selected. Aliases are resolved to their schema field, and fragments and
inline fragments participate in coverage. Field arguments and nested input properties
are tracked for both inline values and variables.
The schema source accepts inline SDL, a file, or a URL. The parameterless
`WithSchemaCoverage()` reads `ProtoTest:Clients:{name}:GraphQL:Schema`.

Shape matching is powered by `ProtoTest.Json`, the protocol-independent matcher
shared with `ProtoTest.Rest`.
