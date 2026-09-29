---
sidebar_position: 6
title: OpenAPI
description: "Compare what your REST tests did against your OpenAPI document, and find the endpoints, responses and properties no test has checked."
---

# OpenAPI

## What it adds

`ProtoTest.OpenApi` compares what your REST tests did against your OpenAPI document, and reports the endpoints, responses and properties no test has touched. It builds on [`ProtoTest.Rest`](./rest/index.md) and listens to the requests and shape assertions REST records.

:::note[Coverage, not validation]
This package reports coverage. It doesn't validate requests or responses against the schema.
:::

```
OpenAPI              GET /api/orders/{id}      12 hits
└ OpenAPI Response      200                     12 hits
  └ OpenAPI Property     id                      12 hits
  └ OpenAPI Property     lines › item › total     0 hits
└ OpenAPI Response      404                      0 hits
```

One glance tells the deliverable: the uncovered rows. Receiving without asserting does not count; only `MatchShape` counts. The full annotated tree is under [What the report contains](#what-the-report-contains).

## Install

```bash
dotnet add package ProtoTest.OpenApi
```

ProtoTest supports .NET 8, 9 and 10. The template targets net10.0 unless you pass -f net8.0 or net9.0. The package resolves its document with `Microsoft.OpenApi` (plus `Microsoft.OpenApi.YamlReader` for YAML) and depends on `ProtoTest.Rest`.

## Compose

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
            .AddCollector<OpenApiCoverageCollector>()));
```

`AddCollector<TCollector>` passes the target name as the first constructor argument and forwards any extra arguments; everything else comes from dependency injection. Registering the same collector type for the same target twice is a no-op, so it reports once.

### The specification source

| Source | Value for `OpenApi:Specification` | Notes |
| --- | --- | --- |
| File path | `control-plane.openapi.json` | resolved as a file when it exists on disk |
| Inline document | the JSON or YAML text itself | handy for small test specs |
| URL | `https://api.example.test/swagger/v1/swagger.json` | a relative URL resolves against the application's `BaseUrl` |

`ProtoTest:Applications:{application}:OpenApi:Specification` takes any of the three. A document that fails to parse throws with the parser's diagnostics.

The application is the one the REST client belongs to: a client registered inside `AddApplication("Api", …)` resolves its specification under `ProtoTest:Applications:Api`, and a host-registered client uses its own target name as the application. If the key is missing or blank, the collector fails when it is constructed:

```
Application 'Api' has no 'OpenApi:Specification' configured. Set 'ProtoTest:Applications:Api:OpenApi:Specification'.
```

The configuration overload receives the host's `IConfiguration` and its registered application targets from DI. Extra `AddCollector` arguments fill the remaining constructor parameters, so a second overload takes the specification source directly:

```csharp
rest.AddClient("Api")
    .AddCollector<OpenApiCoverageCollector>("https://api.example.test/swagger/v1/swagger.json");
```

### Options and keys

| Key | Type | Default / required |
| --- | --- | --- |
| `ProtoTest:Applications:{application}:OpenApi:Specification` | `string` | required; blank throws when the collector is constructed |
| `ProtoTest:Applications:{application}:BaseUrl` | `string?` | optional; used to resolve a relative specification URL only |

There is no options class and no dedicated options section.

### Context API

None. The collector is attached to a REST target and needs no execution-context accessor.

## The tasks

```csharp
var response = await Proto.Context.Rest("Api").GetAsync("/api/orders/42");
response.Should.HaveHttpStatus(HttpStatusCode.OK);
response.Should.MatchShape(new
{
    id = 42,
    lines = new[] { new { total = 10m } }
});
```

The request counts an endpoint and a response hit; the shape assertion is what counts the property hits.

### Going further

#### How a route matches

| Rule | Behaviour |
| --- | --- |
| Normalize | keep the path, strip query and fragment, force a leading slash, trim one trailing slash |
| Prefer | an exact route beats parameter matches; ties break by literal-segment count, then path, case-insensitively |
| Constrain | `int`, `long`, `decimal`/`double`/`float`, `guid`, `bool`, `minlength(n)`, `maxlength(n)` are enforced; unknown constraints match |
| Miss | a route the document does not describe is ignored, as is a method the document does not declare for a matched route |

```
request path → normalize → exact route? → yes → count it
                            → no → parameter route with all constraints satisfied?
                                     → several → most literals wins, then path (ci)
                                     → none → silent, not reported
```

A route parameter accepts the request value unless the contract parameter carries a constraint. `/users/abc` never counts toward `/users/{id:int}`, while a constraint the collector doesn't know cannot reject anything.

#### How a response matches

```
status code → exact key? → yes → count it
              → no → 4XX-style wildcard (ci)? → yes → count it
                     → no → default (ci)? → yes → count it
                            → no → silent, not reported
```

#### How a property matches

- Only `Should.MatchShape` counts. Receiving a field without asserting it leaves it uncovered.
- Array indices are normalized before comparison: `[\d+]` becomes `[]`, and matching is case-insensitive, so `$.lines[0].total` and `$.lines[3].total` both count toward `$.lines[].total`.
- Schema extraction walks the whole document: every media type with a schema adds a `$` baseline row for the body itself; `allOf`, `oneOf` and `anyOf` are traversed at the same path; each property adds `{path}.{name}`; each array item adds `{path}[]`; `$ref`s resolve through the document's components. Recursion and diamond revisits are cut.

#### When it throws and when it stays silent

| Situation | Behaviour |
| --- | --- |
| No `OpenApi:Specification` key, or a blank one | throws when the collector is constructed |
| Document fails to parse | throws with the parser's diagnostics |
| Request route is not in the document, or the method is not declared | silent: ignored, never reported |
| Status code matches no key, wildcard or default | silent: the response is not counted |

#### More than one specification

One collector covers one document. An API with versioned or per-area specifications gets one application target per document, each with its own REST client and collector:

```csharp
builder
    .AddApplication("V1", app => app
        .AddRest(rest => rest
            .AddClient("V1")
            .AddCollector<OpenApiCoverageCollector>()))
    .AddApplication("V2", app => app
        .AddRest(rest => rest
            .AddClient("V2")
            .AddCollector<OpenApiCoverageCollector>()));
```

```json
{
  "ProtoTest": {
    "Applications": {
      "V1": { "OpenApi": { "Specification": "v1.openapi.json" } },
      "V2": { "OpenApi": { "Specification": "v2.openapi.json" } }
    }
  }
}
```

Each collector resolves its specification under its own application, so the two documents report as two targets. To keep the sources in code instead, pass each one directly: `AddCollector<OpenApiCoverageCollector>("v1.openapi.json")`.

#### What the report contains

The collector walks the whole document and reports three levels:

```
OpenAPI              GET /api/orders/{id}      12 hits
└ OpenAPI Response      200                     12 hits
  └ OpenAPI Property     id                      12 hits
  └ OpenAPI Property     lines › item › total     0 hits
└ OpenAPI Response      404                      0 hits
```

- Endpoints use category `OpenAPI` and identifier `{METHOD} {path}` with the method uppercased.
- Responses use category `OpenAPI Response`; the display is `Default response` for `default`, otherwise `{key} response`.
- Properties use category `OpenAPI Property`, identifier = schema path, and a display computed from it: `$` becomes `Response body`, `[]` becomes `.item`, and the remaining segments are joined with `›`.
- One aggregate item records the specification identity: identifier `spec`, display `Specification`, metadata `spec.source` (the configured source, with URL credentials and known token query values removed) and `spec.hash` (SHA-256 of the loaded content). It has no covered verdict, so the coverage totals, run gates and the report percentage ignore it, and a cross-run comparison can tell the same specification from a changed one.

All items are coverage items: covered ones are successful with a hit count, uncovered ones neutral. Register a [report sink](../observability/reporting.md) to see them, and read [Coverage](../observability/coverage.md) for how to use them.

## In the trace and coverage

This package emits **no** trace operations, observations, values or entities of its own. It is a consumer: `CanCollect` matches its target and a `RestResponseData` or `RestShapeMatchData` observation, and it produces report items only. The observations it reads are recorded by the REST integration.

## Skip

The package has no capability descriptor and no package-specific attributes. `[RequiresCapability(...)]` cannot make the collector appear. It is registered on the target builder, and it only produces report items where a REST observation exists.

## Limits

- **Coverage only, never validation.** It counts endpoints, responses and properties a REST observation touched; it does not verify payloads against the schema.
- **Receiving a field is not coverage.** Property hits come only from shape-assertion matches, so a test that reads a response without asserting its shape leaves those properties uncovered.
- **Unmatched routes are silent.** A route the spec doesn't describe, or a method it doesn't declare, is ignored rather than reported.
- **A missing specification fails at construction.** The configuration overload throws when the DI-resolved collector is created, not at report time.
- **Unknown constraints are assumed to match.** Only the listed constraint names are enforced.
- **No base-path rewriting or authentication**, and no refetch on retry. The loader reads the source once.
- **The specification identity row is not coverage.** One aggregate item per target records `spec.source` and `spec.hash`; it carries no verdict, so no total or gate changes because of it.
- **Spec-version support follows the referenced `Microsoft.OpenApi` version, JSON or YAML.**

## Links

- [REST responses and assertions](./rest/responses.md): the shape assertions that produce property hits.
- [Coverage](../observability/coverage.md): collectors and report items.
- [Reporting](../observability/reporting.md): seeing the items in a report.
- The collector registers on the REST client's chain like every other collector; the package tests are in [`tests/ProtoTest.OpenApi.Tests`](https://github.com/MSeys/ProtoTest/tree/main/tests/ProtoTest.OpenApi.Tests).
