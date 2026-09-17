---
sidebar_position: 5
title: Schema coverage
---

# Schema coverage

Point ProtoTest at your GraphQL schema and it reports which types, fields, arguments and input fields your suite actually exercised.

```csharp
builder.AddGraphQL(graphQL => graphQL
    .AddClientFrom("GraphQL", "Api")
    .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "control-plane.graphql")));
```

`schemaSource` is an SDL document — a file path or URL. Leave it out to read it from configuration instead:

```csharp
graphQL.AddClient("GraphQL", "https://api.example.test/graphql").WithSchemaCoverage();
```

```json
{ "ProtoTest": { "Clients": { "GraphQL": { "GraphQL": { "Schema": "schema.graphql" } } } } }
```

Without a schema from either source, the collector throws `InvalidOperationException`.

:::tip Ship the schema with the tests
Copy the `.graphql` file to the output directory so `AppContext.BaseDirectory` finds it:

```xml
<None Include="control-plane.graphql" CopyToOutputDirectory="PreserveNewest" />
```
:::

## What counts as covered

Every executed document — shape-driven, fluent or raw — is parsed and walked against the schema, following fragments and inline fragments. A hit is recorded for:

- every **field** selected,
- every **argument** passed to a field,
- every **input-object field** supplied — including values that arrived through variables.

Introspection types (`__Schema`, `__Type`, …) are ignored.

## What gets reported

Items appear under the category `GraphQL schema`, nested like the schema:

```
GraphQL type          Query
└─ GraphQL field      orders            (returnType, deprecated)
   └─ GraphQL argument   where
GraphQL input type    CreateOrderInput
└─ GraphQL input field   product
```

Each item carries `IsCovered` and a hit count, and fields carry `returnType` and `deprecated` metadata — so the report also tells you whether you're still exercising deprecated fields.

The report is written by whichever [sinks](../../advanced/reporting.md) you register. See [Coverage](../../advanced/coverage.md) for the bigger picture.
