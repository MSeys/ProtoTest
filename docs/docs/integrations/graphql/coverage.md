---
sidebar_position: 5
title: Schema coverage
description: "Point ProtoTest at your GraphQL schema to see which types, fields, arguments and input fields your suite exercised."
---

# Schema coverage

Point ProtoTest at your GraphQL schema and it reports which types, fields, arguments and input fields your suite actually exercised.

| Collector | Reports | Consumes |
| --- | --- | --- |
| `GraphQLSchemaCoverageCollector` (this page) | fields, arguments, input fields per type | each executed document, walked against the schema |
| `GraphQLCoverageCollector` ([Operation coverage](#operation-coverage)) | one covered `GraphQL operation` item per operation identifier, with a hit count | every `graphql.response` observation |

A field is covered when a test selects it. An argument is covered when a test passes it. An input field is covered when a test supplies it, including values that arrived through variables. Introspection types (`__Schema`, `__Type`, …) are ignored.

## What gets reported

Items appear under the category `GraphQL schema`, nested like the schema:

```
GraphQL type          Query                                  (no verdict: totals its fields)
└─ GraphQL field      orders            3 hits                (returnType, deprecated)
   └─ GraphQL argument   where          1 hit                 (declared type)
GraphQL input type    CreateOrderInput                       (no verdict: totals its fields)
└─ GraphQL input field   product        2 hits                (declared type)
```

Each field, argument and input-field item carries `IsCovered` and a hit count; fields add `returnType` and `deprecated` metadata, and arguments and input fields carry their declared `type`. Type rows total their fields' hits and carry no verdict.

One aggregate item records the schema identity: identifier `spec`, display `Schema`, metadata `spec.source` (the configured source) and `spec.hash` (SHA-256 of the loaded SDL). It has no covered verdict, so the type and field totals ignore it, and a cross-run comparison can tell the same schema from a changed one.

## Point at the schema

| Source | Snippet | Notes |
| --- | --- | --- |
| File path | `WithSchemaCoverage("control-plane.graphql")` | copy the file to the output directory (see below) |
| Inline SDL | `WithSchemaCoverage(sdl)` | handy for small test schemas |
| URL or configuration | `WithSchemaCoverage()` plus the `Schema` key | a relative URL resolves against the application's `BaseUrl` |

```csharp
builder.AddApplication("Api", app => app
    .AddGraphQL(graphQL => graphQL
        .AddClient("GraphQL")
        .WithSchemaCoverage(Path.Combine(AppContext.BaseDirectory, "control-plane.graphql"))));
```

`schemaSource` accepts a file path, an inline SDL string, or a URL. Leave it out to read it from configuration instead:

```csharp
builder.AddApplication("Api", app => app
    .AddGraphQL(graphQL => graphQL
        .AddClient("GraphQL", "https://api.example.test/graphql")
        .WithSchemaCoverage()));
```

```json
{ "ProtoTest": { "Applications": { "Api": { "GraphQL": { "Schema": "schema.graphql" } } } } }
```

A relative URL in `Schema` resolves against the application's `BaseUrl`. Without a schema from either source, the collector throws `InvalidOperationException` naming the missing `GraphQL:Schema` key.

:::tip[Ship the schema with the tests]
Copy the `.graphql` file to the output directory so `AppContext.BaseDirectory` finds it:

```xml
<None Include="control-plane.graphql" CopyToOutputDirectory="PreserveNewest" />
```
:::

The collector is added by `WithSchemaCoverage()` or `WithSchemaCoverage(schemaSource)`, which is shorthand for `.AddCollector<GraphQLSchemaCoverageCollector>(schemaSource)`; the parameterless overload binds the schema from configuration instead. Every executed document is parsed and walked against the schema, following fragments and inline fragments (each fragment is walked once per document).

## Operation coverage

`GraphQLCoverageCollector` is the operation-level collector that sits alongside the schema collector. Where the schema collector reports fields, arguments and input fields, this one aggregates every `graphql.response` observation, shape-driven, fluent, raw, or a subscription event, into one covered `GraphQL operation` item per operation identifier, with a hit count. Operation names are case-sensitive. Register it the same way with `.AddCollector<GraphQLCoverageCollector>()`; it ignores other observation kinds such as `graphql.contract.shape`.

The report is written by whichever [sinks](../../observability/reporting.md) you register. See [Coverage](../../observability/coverage.md) for the bigger picture.
