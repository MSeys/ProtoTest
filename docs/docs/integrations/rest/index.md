---
sidebar_position: 1
title: Overview
description: "A per-test HTTP client on IHttpClientFactory, with JSON shape assertions, shared authentication, capture and coverage."
---

# REST

`ProtoTest.Rest` gives each test a named HTTP client built on `IHttpClientFactory`, with JSON shape assertions, the shared [authentication model](./authentication.md), optional request/response capture and endpoint coverage. It brings `ProtoTest.Http` and `ProtoTest.Json` with it.

```bash
dotnet add package ProtoTest.Rest --prerelease
```

ProtoTest targets **.NET 8, 9 and 10**. The template defaults to `net10.0` unless you pass `-f net8.0` (or `net9.0`) to `dotnet new`.

## Registering

Register REST on the host, or under an application so its clients share the application's base URL:

```csharp
builder.AddRest();                                    // host-level clients only

builder.AddApplication("Api", app => app.AddRest(rest => rest
    .AddClient("Api")                                 // the application's default REST client
    .AddClient("Billing", endpoint: "BillingApi")));  // joins BaseUrl with Endpoints:BillingApi
```

Repeated registration never errors: the lifecycle hook and keyed options register once, while every `AddRest` callback still runs and composes more clients.

### `AddClient` overloads

| Overload | Base address |
| --- | --- |
| `AddClient(name = "Default", baseUrl = null, configure = null, endpoint = null)` | the explicit `baseUrl`, else the application's `BaseUrl` joined with `Endpoints:{endpoint}` |
| `AddClient(name, Func<ProtoExecutionContext, Uri> resolver, configure = null)` | resolved per request from the running test |
| `AddClient(name, Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> resolver, configure = null)` | async per-request resolution |
| `AddClientFrom(name, sourceClientName, basePath = null)` | another integration's registered client, optionally rooted at `basePath` |

The **endpoint default rule**: `endpoint` names the key under `ProtoTest:Applications:{application}:Endpoints` to append to the application's `BaseUrl`. Inside `AddApplication` it defaults to the client name; a host-level client registered with `AddRest` has no endpoint default. An explicit non-absolute base URL throws `ArgumentException`.

`AddClientFrom` reuses the transport of an HTTP client another integration registered — an in-process ASP.NET Core server, for example — and the source client must have a base address when the alias resolves. See [ASP.NET Core](../aspnetcore.md).

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

## Options and keys

| Section | Key | Default |
| --- | --- | --- |
| `ProtoTest:Rest:Responses` | `MaxResponseBodyBytes` | `10485760` (10 MiB) |
| | `MaxDiagnosticBodyLength` | `65536` |
| `ProtoTest:Rest:Attachments` | `CaptureRequestBodies`, `CaptureResponses`, `CaptureExpectedShapes` | `true` |
| | `RedactSensitiveData` | `true` |
| | `MaxDiagnosticBodyLength` | `65536` |
| | `SensitiveHeaders` | `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` |
| | `SensitiveQueryParameters` | `access_token`, `refresh_token`, `token`, `apiKey`, `api_key`, `key` |
| | `SensitiveJsonProperties` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key` |

Set them in code with `ConfigureResponses(...)` and `CaptureAttachments(...)`, or in configuration. Code callbacks run in registration order and repeated calls compose; the known section is then bound over the result, so **configuration wins over code**. Both option types are shared with GraphQL — each protocol owns its own keyed instance and section. The types themselves default to the shared `ProtoTest:Http:Responses` and `ProtoTest:Http:Attachments` sections when a protocol does not name its own; every protocol that ships here names one.

## Context API

```csharp
RestRequestBuilder Rest(this ProtoExecutionContext context, string? clientName = null);
```

`Proto.Context.Rest(name)` resolves the client in this order: the requested name, the client bound by `[Application(…)]` for REST, the application's first registered REST client, then `"Default"`. A client with no base address and no owner for its address falls back to the application's in-process transport, rooted at the endpoint the client registered, then the requested name — looking up `ProtoTest:Applications:{application}:Endpoints:{name}`. A per-test resolver beats `HttpClient.BaseAddress` at request time. If nothing resolves, the call throws `InvalidOperationException`.

## Quick start

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

## Going further

- **Authentication** — `.Auth<T>(...)`, `.WithoutAuth()`, and `[Auth<T>]` on the class or method: [Authentication](./authentication.md).
- **Attachments** — `.CaptureAttachments()` records sanitized request, response and expected-shape attachments: [Attachments and coverage](./attachments.md).
- **Coverage** — attach `.AddCollector<RestCoverageCollector>()` to the client; [OpenAPI coverage](../openapi.md) consumes the shape assertions' matched paths.
- **Multiple clients** — pass `.Rest("Billing")`, or bind one for the whole test with `[Application("Api", "Rest:Billing")]`.
- **In-process server** — `AddAspNetCoreServer<Program>()` plus a client with no URL reuses its transport, or point `AddClientFrom` at another client explicitly.

## Tracing and coverage

Each request records an `http.request` operation (`REST · {METHOD} {route}`) under the `client:HttpClient:{target}` entity, with the method, route, sanitized URL, header count and response status. Assertions record `assert.http.status` and `assert.json.shape` as children of that request. Observations: `http.response` for every response (method, route template, status, sanitized body and headers, duration), `http.failure` when sending or URI building fails, and `http.contract.shape` when a shape assertion matches. `RestCoverageCollector` turns `http.response` observations into `REST` coverage items. See [ProtoTrace](../../observability/prototrace.md) and [Coverage](../../observability/coverage.md).

## Skip

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "REST")]
```

## Limits

- Shape assertions are positive-only, and `Should` / `ShouldNot` expose status assertions only.
- Response bodies are always buffered whole in memory; there is no streaming read API.
- Route tokens are `{name}` over ASCII letters, digits and `_`.
- Registering the same client name twice keeps the first registration; `TryAddResponseOptions` is first-wins for the keyed instance, though configuration callbacks compose.
- No retry or resilience-policy layer.

## Next

- [Building requests](./requests.md) — verbs, route templates, bodies and headers.
- [Responses and assertions](./responses.md) — status, JSON shapes and typed reads.
- [Authentication](./authentication.md) — per-request, per-class and composed authenticators.
- [Attachments and coverage](./attachments.md) — what gets captured, redacted and reported.

The full flows live in the demo: [DeliveryJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/DeliveryJourney.cs) and [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs).
