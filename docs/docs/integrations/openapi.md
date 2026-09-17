---
sidebar_position: 7
title: OpenAPI
---

# OpenAPI

`ProtoTest.OpenApi` compares what your REST tests did against your OpenAPI document, and reports the parts of the contract no test has touched.

```bash
dotnet add package ProtoTest.OpenApi
```

It builds on [`ProtoTest.Rest`](./rest/index.md) — it listens to the requests and shape assertions REST records.

:::note Coverage, not validation
This package reports coverage. It doesn't validate requests or responses against the schema.
:::

## Registering

Attach the collector to the REST client whose API the document describes:

```csharp
builder
    .ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["ProtoTest:Applications:Api:OpenApi:Specification"] =
                Path.Combine(AppContext.BaseDirectory, "control-plane.openapi.json")
        }))
    .AddApplication("Api", app => app
        .AddRest(rest => rest
            .AddClient("Api")
            .WithCollector<OpenApiCoverageCollector>()));
```

The collector reads `ProtoTest:Applications:{application}:OpenApi:Specification`, where the application is the one the REST client belongs to. In a real project you'd usually put that in `appsettings.json` instead. The value can be a file path, a URL, or the document itself as JSON or YAML. A relative URL is resolved against the application's `ProtoTest:Applications:{application}:BaseUrl` — handy for pointing at `/swagger/v1/swagger.json` on a deployed API.

If the key is missing you get:

```
Application 'Api' has no 'OpenApi:Specification' configured. Set 'ProtoTest:Applications:Api:OpenApi:Specification'.
```

A document that fails to parse throws with the parser's diagnostics.

### Passing the document directly

`WithCollector` forwards extra arguments to the collector's constructor, which has overloads for a source string and a parsed document (the configuration route is the one exercised by the sample suite):

```csharp
rest.AddClient("Api")
    .WithCollector<OpenApiCoverageCollector>("https://api.example.test/swagger/v1/swagger.json");
```

```csharp
OpenApiCoverageCollector(string targetName, IConfiguration configuration)
OpenApiCoverageCollector(string targetName, string openApiSpecSource)
OpenApiCoverageCollector(string targetName, OpenApiDocument document)
```

## What it reports

The collector walks the **entire** document — not just what was called — and reports three nested levels:

```
OpenAPI            GET /api/orders/{id}        12 hits   ✓
└ OpenAPI Response    200                          12 hits   ✓
  └ OpenAPI Property     $.id                         12 hits   ✓
  └ OpenAPI Property     $.lines[].total               0 hits   ○
└ OpenAPI Response    404                           0 hits   ○
OpenAPI            DELETE /api/orders/{id}      0 hits   ○
```

- **Endpoints** are matched by method and route template, so `/api/orders/42` counts toward `/api/orders/{id}`.
- **Responses** match the exact status code first, then a range like `4XX`, then `default`.
- **Properties** count only when a [`ShouldMatchShape`](./rest/responses.md#shouldmatchshape) assertion actually matched them. Receiving a field doesn't count; asserting it does. Array indices are normalised, so `$.lines[0].total` and `$.lines[3].total` both count toward `$.lines[].total`.

Uncovered items are reported with a neutral status, covered ones as successful with their hit count. Register a [report sink](../advanced/reporting.md) to see them, and read [Coverage](../advanced/coverage.md) for how to use them.
