---
sidebar_position: 2
title: Queries and mutations
---

# Queries and mutations

There are three ways to describe an operation. Pick per test — they all end in the same `ExecuteAsync()`.

| Style | Best for |
| --- | --- |
| **Shape-driven** — `Query("root", args).Select(shape)` | one root field, where the selection is also what you assert |
| **Fluent** — `Query("Name", q => q.Field(...))` | several root fields, aliases, connections with filters and paging |
| **Raw** — `Request("query { … }")` | fragments, directives, anything the builders don't cover |

## Shape-driven operations

```csharp
GraphQLRequestBuilder Query(string rootField, object? arguments = null, string? operationName = null);
GraphQLRequestBuilder Mutation(string rootField, object? arguments = null, string? operationName = null);
GraphQLRequestBuilder Subscription(string rootField, object? arguments = null, string? operationName = null);

GraphQLRequestBuilder Select<TShape>(TShape selectionShape);
GraphQLRequestBuilder Select<TShape>();

Task<GraphQLResponse> ExecuteAsync(CancellationToken cancellationToken = default);
Task<GraphQLResponse> ExpectAsync<TShape>(TShape expectedShape, CancellationToken cancellationToken = default);
```

The operation name defaults to the root field in PascalCase (`createOrder` → `CreateOrder`).

### Select, then execute

```csharp
using var response = await Proto.Context.GraphQL()
    .Query("me")
    .Select(new { id = Gql.Field, email = Gql.Field, role = Gql.Field })
    .ExecuteAsync();
```

`Gql.Field` is a placeholder meaning "select this scalar, I don't care about its value".

### Select and assert in one step

`ExpectAsync(shape)` is `Select(shape)` + `ExecuteAsync()` + `ShouldMatchData(shape)`:

```csharp
using var controlPlane = await Proto.Context.GraphQL()
    .Query("controlPlane")
    .ExpectAsync(new
    {
        userCount = 1,
        workspaceCount = 1,
        releaseCount = 0,
        monthlyRecurringRevenue = 199m
    });

controlPlane.ShouldHaveNoErrors();
```

Arrays work too — the element shape becomes the selection, and the whole array is asserted:

```csharp
using var workspaces = await Proto.Context.GraphQL()
    .Query("workspaces")
    .ExpectAsync(new[]
    {
        new { name = "analytics", region = "eu-central", plan = "growth" }
    });
```

If the response fails the shape, `ExpectAsync` disposes it and throws.

### Select a type

```csharp
private sealed record ViewerSelection(string Id, string Email, string Role);

using var response = await Proto.Context.GraphQL()
    .Query("me")
    .Select<ViewerSelection>()
    .ExecuteAsync();

var viewer = response.ReadDataAs<ViewerSelection>();
```

### How a shape becomes a selection set

- Every public property becomes a field, named by `[JsonPropertyName]` or else camelCase. `[JsonIgnore]` properties are skipped.
- Recursion stops at a **leaf**: `Gql.Field`, any `JsonValue` matcher, or a value of a primitive, enum, `string`, `decimal`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `Guid` or `Uri` type.
- An array or enumerable is unwrapped to its first element, so `new[] { new { id = Gql.Field } }` selects `{ id }`.
- `IDictionary<string, …>` shapes are supported.
- A nested object with no properties throws `ArgumentException` — GraphQL doesn't allow an empty selection.

:::caution
Don't put `Gql.Enum(...)` inside a *selection* shape — it isn't treated as a leaf. It belongs in *arguments*.
:::

### Arguments and variables

Anything in the arguments object is inlined as a literal — except `Gql.Variable(...)`, which declares an operation variable and sends the value separately:

```csharp
.Mutation("createOrder", new
{
    input = Gql.Variable("CreateOrderInput!", new CreateOrderRequest("notebook", 2, 12.50m))
})
```

produces `mutation CreateOrder($input: CreateOrderInput!) { createOrder(input: $input) { … } }`.

The type string is parsed when you create the variable, so a typo fails immediately. You can also build types with `GqlType`:

```csharp
Gql.Variable(GqlType.Named("CreateOrderInput").NonNull(), order)
Gql.Variable(GqlType.Id.NonNull().List(), ids)        // [ID!]
```

`GqlType` has `Id`, `String`, `Int`, `Float`, `Boolean`, `Upload` and `Named(name)`.

For enum literals in arguments, use `Gql.Enum("DESC")`.

## Fluent operations

```csharp
GraphQLRequestBuilder Query(string? name, Action<GraphQLOperationBuilder> configure);
GraphQLRequestBuilder Mutation(string? name, Action<GraphQLOperationBuilder> configure);
GraphQLRequestBuilder Subscription(string? name, Action<GraphQLOperationBuilder> configure);
```

The second parameter decides the overload: a lambda gives you the fluent builder, an object gives you shape-driven.

```csharp
using var response = await Proto.Context.GraphQL()
    .Query("Dashboard", query => query
        .Variable("tenant", "String!")
        .Field("me", me => me.Fields("id", "email"))
        .Field("workspaces", workspaces => workspaces
            .Alias("active")
            .Argument("tenant", Gql.Var("tenant"))
            .Select(workspace => workspace
                .Fields("name", "region")
                .Field("owner", owner => owner.Fields("email")))))
    .Variables(new { tenant = "acme" })
    .ExecuteAsync();
```

| Builder | Members |
| --- | --- |
| operation | `Variable(name, type)`, `Field(name, configure?)`, `Connection(name, configure)` |
| field | `Alias(alias)`, `Argument(name, value)`, `Fields(params names)`, `Select(configure)` |
| selection | `Field(name, configure?)`, `Fields(params names)` |

`Gql.Var("tenant")` references a declared variable (`$tenant`); supply its value with `.Variables(...)`.

### Connections

`Connection` understands the common cursor-connection shape — filtering, ordering and paging:

```csharp
using var response = await Proto.Context.GraphQL()
    .Query("FindNotebooks", query => query
        .Connection("orders", orders => orders
            .Where(filter => filter.Contains("product", "notebook"))
            .OrderBy(order => order.Descending("total"))
            .First(1)
            .Nodes("product", "total", "status")
            .PageInfo("hasNextPage", "hasPreviousPage")
            .TotalCount()))
    .ExecuteAsync();

response.ShouldHaveNoErrors().ShouldMatchData(new
{
    orders = new
    {
        nodes = new[] { new { product = "notebook-pro", total = 40m, status = "pending" } },
        pageInfo = new { hasNextPage = true, hasPreviousPage = false },
        totalCount = 2
    }
});
```

| Connection member | Produces |
| --- | --- |
| `First(n)`, `Last(n)`, `After(cursor)`, `Before(cursor)` | paging arguments |
| `Where(filter => …)` | a `where:` argument |
| `OrderBy(order => order.Ascending(f).Descending(g))` | an `order:` argument with `ASC`/`DESC` enums |
| `Nodes(params fields)` / `Nodes(field => …)` | `nodes { … }` |
| `PageInfo(params fields)` | `pageInfo { … }` — all four standard fields when none are given |
| `TotalCount()` | `totalCount` |

The filter builder emits the `{ field: { op: value } }` convention used by Hot Chocolate: `Equal`, `NotEqual`, `Contains`, `StartsWith`, `EndsWith`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `In`, plus `Nested(field, …)`, `Some(field, …)` for lists, and `Or(...)`.

:::note Fluent responses aren't unwrapped
With shape-driven operations, `ShouldMatchData` compares against the **root field's value**. With fluent and raw operations there's no single root, so it compares against the whole `data` object — which is why the example above wraps its shape in `orders = …`.
:::

## Raw documents

```csharp
using var response = await Proto.Context.GraphQL()
    .Request(
        """
        query ViewerCard {
          viewer: me { ...ViewerFields }
        }

        fragment ViewerFields on UserResponse { id email role }
        """,
        operationName: "ViewerCard")
    .ExecuteAsync();

response.ShouldHaveNoErrors().ShouldMatchData(new
{
    viewer = new
    {
        id = JsonValue.NotNull(),
        email = JsonValue.StringContaining("@example.test"),
        role = "member"
    }
});
```

## Headers and variables

```csharp
GraphQLRequestBuilder Header(string name, string value);
GraphQLRequestBuilder Variables(object variables);
```

## File uploads

ProtoTest implements the GraphQL multipart request spec. Put `Gql.Upload(...)` in the arguments and it's declared as `Upload!` automatically:

```csharp
var expected = new
{
    fileName = "example.txt",
    contentType = "text/plain",
    length = JsonValue.GreaterThan(0)
};

using var uploaded = await Proto.Context.GraphQL()
    .Mutation("uploadDocument", new
    {
        file = Gql.Upload("ProtoTest GraphQL"u8.ToArray(), "example.txt", "text/plain")
    })
    .Select(expected)
    .ExecuteAsync();

uploaded.ShouldHaveNoErrors().ShouldMatchData(expected);
```

```csharp
static GraphQLUpload Upload(ReadOnlyMemory<byte> content, string fileName, string contentType = "application/octet-stream");
static GraphQLUpload Upload(Func<Stream> openRead, string fileName, string contentType = "application/octet-stream");
```

Uploads nested inside `.Variables(...)` are discovered too. The request is sent as `multipart/form-data` with the `GraphQL-preflight: 1` header that CSRF-protected servers expect.

## Transport details

Queries and mutations are sent as `POST` with `Accept: application/graphql-response+json, application/json;q=0.9`. Calling `ExecuteAsync()` on a subscription throws — use [`SubscribeAsync()`](./subscriptions.md).
