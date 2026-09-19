---
sidebar_position: 5
title: Schema coverage
description: "Point ProtoTest at your GraphQL schema to see which types, fields, arguments and input fields your suite exercised."
---

# Schema coverage

Point ProtoTest at your GraphQL schema and it reports which types, fields, arguments and input fields your suite actually exercised.

```csharp
builder.AddApplication("Api", app => app
    .AddGraphQL(graphQL => graphQL
        .AddClient("GraphQL")
        .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "control-plane.graphql"))));
```

`schemaSource` is an SDL document — a file path or URL. Leave it out to read it from configuration instead:

```csharp
builder.AddApplication("Api", app => app
    .AddGraphQL(graphQL => graphQL
        .AddClient("GraphQL", "https://api.example.test/graphql")
        .WithSchemaCoverage()));
```

```json
{ "ProtoTest": { "Applications": { "Api": { "GraphQL": { "Schema": "schema.graphql" } } } } }
```

Without a schema from either source, the collector throws `InvalidOperationException`.

:::tip[Ship the schema with the tests]
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

## Operation coverage

`GraphQLCoverageCollector` is the operation-level collector that sits alongside the schema collector. Where the schema collector reports fields, arguments and input fields, this one aggregates every `graphql.response` observation — shape-driven, fluent, raw, or a subscription event — into one covered `GraphQL operation` item per operation identifier, with a hit count. Register it the same way with `.AddCollector<GraphQLCoverageCollector>()`; it ignores other observation kinds such as `graphql.contract.shape`.

## What gets reported

Items appear under the category `GraphQL schema`, nested like the schema:

```
GraphQL type          Query
└─ GraphQL field      orders            (returnType, deprecated)
   └─ GraphQL argument   where
GraphQL input type    CreateOrderInput
└─ GraphQL input field   product
```

Each field, argument and input-field item carries `IsCovered` and a hit count, and fields carry `returnType` and `deprecated` metadata — so the report also tells you whether you're still exercising deprecated fields. Type rows aggregate the fields under them: they carry a hit count but no `IsCovered` verdict of their own, and the report summary leaves those aggregate rows out of its coverage totals.

The report is written by whichever [sinks](../../observability/reporting.md) you register. See [Coverage](../../observability/coverage.md) for the bigger picture.
