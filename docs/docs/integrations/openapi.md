---
sidebar_position: 6
title: OpenAPI
description: "Compare what your REST tests did against your OpenAPI document, and find the endpoints, responses and properties no test has checked."
---

# OpenAPI

`ProtoTest.OpenApi` compares what your REST tests did against your OpenAPI document, and reports the endpoints, responses and properties no test has touched. It builds on [`ProtoTest.Rest`](./rest/index.md) — it listens to the requests and shape assertions REST records.

:::note[Coverage, not validation]
This package reports coverage. It doesn't validate requests or responses against the schema.
:::

## Install

```bash
dotnet add package ProtoTest.OpenApi --prerelease
```

ProtoTest targets .NET 8, 9 and 10; the template defaults to `net10.0` unless `-f` is passed. The package resolves its document with `Microsoft.OpenApi.Readers` and depends on `ProtoTest.Rest`.

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
            .AddCollector<OpenApiCoverageCollector>()));
```

`AddCollector<TCollector>` passes the target name as the first constructor argument and forwards any extra arguments; everything else comes from dependency injection. Registering the same collector type for the same target twice is a no-op, so it reports once.

### The specification source

`ProtoTest:Applications:{application}:OpenApi:Specification` may be a local file path, the document itself as JSON or YAML, or an http(s) URL. A relative URL is resolved against the application's `ProtoTest:Applications:{application}:BaseUrl` — handy for pointing at `/swagger/v1/swagger.json` on a deployed API. A document that fails to parse throws with the parser's diagnostics.

The application is the one the REST client belongs to: `ProtoTest:Applications:{scope}:Application` maps a client target to an application, falling back to the target name. If the key is missing or blank, the collector fails when it is constructed:

```
Application 'Api' has no 'OpenApi:Specification' configured. Set 'ProtoTest:Applications:Api:OpenApi:Specification'.
```

### Constructors

```csharp
OpenApiCoverageCollector(string targetName, IConfiguration configuration, IEnumerable<ProtoApplicationTarget> applicationTargets)
OpenApiCoverageCollector(string targetName, string openApiSpecSource)
OpenApiCoverageCollector(string targetName, OpenApiDocument document)
```

The configuration overload receives the host's `IConfiguration` and its registered application targets from DI. Extra `AddCollector` arguments fill the remaining constructor parameters:

```csharp
rest.AddClient("Api")
    .AddCollector<OpenApiCoverageCollector>("https://api.example.test/swagger/v1/swagger.json");
```

## Options and keys

| Key | Type | Default / required |
| --- | --- | --- |
| `ProtoTest:Applications:{application}:OpenApi:Specification` | `string` | required; blank throws when the collector is constructed |
| `ProtoTest:Applications:{application}:BaseUrl` | `string?` | optional; used to resolve a relative specification URL only |
| `ProtoTest:Applications:{scope}:Application` | `string?` | optional; maps a REST client target to an application, defaulting to the target name |

There is no options class and no dedicated options section.

## Context API

None. The collector is attached to a REST target and needs no execution-context accessor.

## Quick start

```csharp
var response = await Proto.Context.Rest("Api").GetAsync("/api/orders/42");
response.Should.HaveHttpStatus(HttpStatusCode.OK);
response.ShouldMatchShape(new
{
    id = 42,
    lines = new[] { new { total = 10m } }
});
```

The request counts an endpoint and a response hit; the shape assertion is what counts the property hits.

## Going further

### How a route matches

Route matching normalizes both sides before comparing: an absolute URI is reduced to its path, the query and fragment are stripped, a leading `/` is forced and one trailing `/` is trimmed. An exact route beats parameter matches; ties break by literal-segment count, then path, case-insensitively.

A route parameter accepts the request value unless the contract parameter carries a constraint. These are enforced: `int`, `long`, `decimal`/`double`/`float`, `guid`, `bool`, `minlength(n)` and `maxlength(n)`. Unknown constraints are treated as matching, so `/users/abc` never counts toward `/users/{id:int}`, while a constraint the collector doesn't know cannot reject anything.

A route the document doesn't describe is ignored, and a method the document doesn't declare for a matched route is not counted — the report only enumerates spec operations.

### How a response matches

The exact status code is looked up first, then a case-insensitive wildcard like `4XX`, then a case-insensitive `default`.

### How a property matches

- Only shape assertions count. A property is covered when a `ShouldMatchShape` assertion actually matched it; receiving a field in a response body is not coverage.
- Array indices are normalized before comparison: `[\d+]` becomes `[]`, and matching is case-insensitive, so `$.lines[0].total` and `$.lines[3].total` both count toward `$.lines[].total`.
- Schema extraction walks the whole document: every media type with a schema adds a `$` baseline row for the body itself; `allOf`, `oneOf` and `anyOf` are traversed at the same path; each property adds `{path}.{name}`; each array item adds `{path}[]`; `$ref`s resolve through the document's components. Recursion and diamond revisits are cut.

### What the report contains

The collector walks the **entire** document — not just what was called — and reports three nested levels:

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

All items are coverage items: covered ones are successful with a hit count, uncovered ones neutral. Register a [report sink](../observability/reporting.md) to see them, and read [Coverage](../observability/coverage.md) for how to use them.

## Tracing and coverage

This package emits **no** trace operations, observations, values or entities of its own. It is a consumer: `CanCollect` matches its target and a `RestResponseData` or `RestShapeMatchData` observation, and it produces report items only. The observations it reads are recorded by the REST integration.

## Skip

The package has no capability descriptor and no package-specific attributes. `[RequiresCapability(...)]` cannot make the collector appear — it is registered on the target builder, and it only produces report items where a REST observation exists.

## Limits

- **Coverage only, never validation.** It counts endpoints, responses and properties a REST observation touched; it does not verify payloads against the schema.
- **Receiving a field is not coverage.** Property hits come only from shape-assertion matches, so a test that reads a response without asserting its shape leaves those properties uncovered.
- **Unmatched routes are silent.** A route the spec doesn't describe, or a method it doesn't declare, is ignored rather than reported.
- **A missing specification fails at construction.** The configuration overload throws when the DI-resolved collector is created, not at report time.
- **Unknown constraints are assumed to match.** Only the listed constraint names are enforced.
- **No base-path rewriting or authentication**, and no refetch on retry — the loader reads the source once.
- **Spec-version support is whatever `Microsoft.OpenApi.Readers` 1.6.31 parses.**

## Links

- [REST responses and assertions](./rest/responses.md) — the shape assertions that produce property hits.
- [Coverage](../observability/coverage.md) — collectors and report items.
- [Reporting](../observability/reporting.md) — seeing the items in a report.
- The demo registers the collector in [`samples/ProtoTest.Demo/Setup.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs).
