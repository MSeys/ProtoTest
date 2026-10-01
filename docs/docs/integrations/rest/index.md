---
sidebar_position: 1
title: Test REST APIs in .NET
sidebar_label: Overview
description: "A per-test HTTP client on IHttpClientFactory, with JSON shape assertions, shared authentication, capture and coverage."
---

# Test REST APIs in .NET

`ProtoTest.Rest` gives each test a named HTTP client with JSON shape assertions.

```csharp
[Application("Api")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task Creates_an_order()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 2 })
            .PostAsync("/api/orders");

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
    }
}
```

Run it with `dotnet test`. A green run prints `Passed Creates_an_order`, and the trace lands at `TestResults/prototest-{runId}.prototrace` with an `http.request` operation for the call.

## Install

```bash
dotnet add package ProtoTest.Rest
```

See [Installation](../../getting-started/installation.md) for the supported .NET versions.

## What it adds

Each test gets a named HTTP client built on `IHttpClientFactory`, with JSON shape assertions, the shared [authentication model](./authentication.md), optional request/response capture and endpoint coverage. It brings `ProtoTest.Http` and `ProtoTest.Json` with it.

## Compose

Register REST on the host, or under an application so its clients share the application's base URL:

```csharp
builder.AddRest();                                    // host-level clients only

builder.AddApplication("Api", app => app.AddRest(rest => rest
    .AddClient("Api")                                 // the application's default REST client
    .AddClient("Billing", endpoint: "BillingApi")));  // joins BaseUrl with Endpoints:BillingApi
```

Calling `AddRest` twice does not throw. The shared setup runs once. Each callback still adds its clients.

### `AddClient` overloads

| Overload | Base address |
| --- | --- |
| `AddClient(name = null, baseUrl = null, configure = null, endpoint = null)` | the explicit `baseUrl`, else the application's `BaseUrl` joined with `Endpoints:{endpoint}` |
| `AddClient(name, Func<ProtoExecutionContext, Uri> resolver, configure = null)` | resolved per request from the running test |
| `AddClient(name, Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> resolver, configure = null)` | async per-request resolution |

Omit the name when the application has one REST client. Pass a name only for several targets (`Rest("Billing")`, `[Application("Api", "Rest:Billing")]`).

There is **no endpoint default**: `endpoint` names the key under `ProtoTest:Applications:{application}:Endpoints` to append to the application's `BaseUrl`. Without it the base URL is used as-is: an explicit `baseUrl`, or the application's `BaseUrl`. An explicit non-absolute base URL throws `ArgumentException`.

### Base URL from configuration

```json
{
  "ProtoTest": {
    "Applications": {
      "Api": {
        "BaseUrl": "https://staging.example.test/",
        "Endpoints": { "BillingApi": "/api/billing" }
      }
    }
  }
}
```

## The tasks

The `Creates_an_order` test above is the whole pattern: build, send with a verb, assert. The pages below cover each step.

### Going further

- **Authentication** - `.Auth<T>(...)`, `.WithoutAuth()`, and `[Auth<T>]` on the class or method: [Authentication](./authentication.md).
- **Attachments** - `.CaptureAttachments()` records sanitized request, response and expected-shape attachments: [Attachments and coverage](./attachments.md).
- **Coverage** - attach `.AddCollector<RestCoverageCollector>()` to the client. [OpenAPI coverage](../openapi.md) consumes the shape assertions' matched paths. `.AddCollector<RestTrafficCoverageCollector>()` reports the fields no shape mentioned: [Coverage](../../observability/coverage.md#traffic-coverage-observed-but-unasserted).
- **Multiple clients** - pass `.Rest("Billing")`, or bind one for the whole test with `[Application("Api", "Rest:Billing")]`.
- **In-process server** - `AddAspNetCoreServer<Program>()` plus a client with no URL reuses its transport automatically. A configured `BaseUrl` wins and the in-process server stays stopped.

### Reference: resolution and options

```csharp
RestRequestBuilder Rest(this ProtoExecutionContext context, string? clientName = null);
```

`Proto.Context.Rest(name)` picks the client in this order:

| Step | What it tries |
| --- | --- |
| 1 | the requested name |
| 2 | the client bound by `[Application(…)]` for REST |
| 3 | the application's first registered REST client |
| 4 | `"Default"` |

A requested name first tries its application-qualified form, then the name as given. When exactly one client of the protocol registered that name on another application, the call reaches it. `Rest("Api")` in a test that selects the `Dashboard` application reaches `Csms:Api` when `Dashboard` has no `Api` client. When two applications share the name, the call fails and names both qualified candidates. Qualify the call then (`Rest("Csms:Api")`). An already qualified name is exact.

The client's base address is the application's, resolved with the shared precedence: an address a started piece published wins over configuration. A client with no base address and no owner for its address falls back to the application's in-process transport. That transport is rooted at the endpoint the client registered, or else at the requested name, looked up as `ProtoTest:Applications:{application}:Endpoints:{name}`. A per-test resolver beats `HttpClient.BaseAddress` at request time.

A client whose address resolves is built over a test-owned handler, so its cookie jar carries only that test's session. Parallel tests never share sign-in state. If nothing resolves, the call throws `InvalidOperationException` listing the protocol's registered client names.

| Section | Key | Default |
| --- | --- | --- |
| `ProtoTest:Rest:Responses` | `MaxResponseBodyBytes` | `10485760` (10 MiB) |
| | `MaxDiagnosticBodyLength` | `65536` |
| `ProtoTest:Rest:Attachments` | `CaptureRequestBodies`, `CaptureResponses`, `CaptureExpectedShapes` | `true` |
| | `RedactSensitiveData` | `true` |
| | `MaxDiagnosticBodyLength` | `65536` |
| | `SensitiveHeaders` | `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` |
| | `SensitiveQueryParameters` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key`, `authorization`, `cookie`, `connectionString`, `clientSecret`, `client_secret`, `id_token`, `key` |
| | `SensitiveJsonProperties` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key`, `authorization`, `cookie`, `connectionString`, `clientSecret`, `client_secret`, `id_token` |

Set them in code or configuration. Configuration binds last, so it wins over code. Both option types are shared with GraphQL, and each protocol owns its own keyed instance and section. When a protocol names no section of its own, the types fall back to the shared `ProtoTest:Http:Responses` and `ProtoTest:Http:Attachments` sections. Every protocol here names one.

## In the trace and coverage

```text
http.request REST · POST /api/orders · 201 Created
├─ assert.http.status (expected and actual agree)
├─ assert.json.shape (5 properties matched at once)
└─ http.response (observation: status, sanitized body and headers, duration)
```

Each request records an `http.request` operation (`REST · {METHOD} {route}`) under the protocol-scoped client entity (`client:System.Net.Http.HttpClient:Rest:{target}`). It carries the method, route, sanitized URL, header count and response status. Assertions record `assert.http.status`, `assert.http.content_type`, `assert.http.header`, `assert.http.cookie`, `assert.http.redirect_location` and `assert.json.shape` as children of that request.

The observations are:

- `http.response` for every response: method, route template, status, sanitized body and headers, duration.
- `http.failure` when sending or URI building fails.
- `http.contract.shape` when a shape assertion matches.

`RestCoverageCollector` turns `http.response` observations into `REST` coverage items. `RestTrafficCoverageCollector` consumes `http.response` and `http.contract.shape` for its observed-but-unasserted section. See [ProtoTrace](../../observability/prototrace.md) and [Coverage](../../observability/coverage.md).

## Skip

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "REST")]
```

## Limits

| Limit | Matters when | Severity |
| --- | --- | --- |
| Shape assertions are positive-only. `Should` and `ShouldNot` both expose the status, content type, header, cookie and redirect-location assertions. `MatchShape` is the only positive-only member. | Asserting that a field is absent or that a value differs. | The test cannot express it. |
| Response bodies are always buffered whole in memory. There is no streaming read API. | Downloading large payloads. | Memory grows with the body size. |
| Route tokens are `{name}` over ASCII letters, digits and `_`. | Routes use other characters. | The route does not match. |
| Registering the same client name twice keeps the first registration. `TryAddResponseOptions` is first-wins for the keyed instance, though configuration callbacks compose. | Overlapping registrations compose the same client name. | The later registration is ignored. |
| No retry or resilience-policy layer. | Calling a flaky endpoint. | A transient failure fails the test. |

## Next

- [Building requests](./requests.md) - verbs, route templates, bodies and headers.
- [Responses and assertions](./responses.md) - status, JSON shapes and typed reads.
- [Authentication](./authentication.md) - per-request, per-class and composed authenticators, and the built-in test user.
- [Attachments and coverage](./attachments.md) - what gets captured, redacted and reported.

The same flows live in the sample: [ProjectsJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/ProjectsJourney.cs) and [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/Setup.cs).
