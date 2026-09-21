# ProtoTest.GraphQL

GraphQL queries, mutations and subscriptions with named clients, shape assertions and schema coverage.

```bash
dotnet add package ProtoTest.GraphQL
```

```csharp
using var response = await Proto.Context.GraphQL()
    .Query("products", new { first = 10 })
    .ExpectAsync(new
    {
        nodes = new[] { new { id = JsonValue.NotNull(), name = "Notebook" } }
    });

response.ShouldHaveNoErrors();
```

The package also supports raw documents, file uploads and subscriptions over WebSocket or SSE. Schema coverage is available when an SDL source is configured.

## Learn more

- [GraphQL integration](https://prototest.dev/docs/integrations/graphql/)
- [GraphQL subscriptions](https://prototest.dev/docs/integrations/graphql/subscriptions)
- [Shape matching](https://prototest.dev/docs/foundation/shape-matching)
