---
sidebar_position: 1
title: Overview
---

# REST

`ProtoTest.Rest` gives each test an HTTP client built on `IHttpClientFactory`, with response-shape assertions, a shared authentication model and automatic request/response capture.

```bash
dotnet add package ProtoTest.Rest
```

It brings `ProtoTest.Http` (the shared authentication building blocks) and `ProtoTest.Json` (shape matching) with it — you don't add those yourself.

## Registering a client

```csharp
builder.AddRest(rest =>
{
    rest.AddClient("Api", "https://api.example.test/");
    rest.AddClient("Billing", "https://billing.example.test/");
});
```

`AddClient` has three overloads:

```csharp
IProtoTargetBuilder AddClient(
    string name = "Default",
    string? baseUrl = null,
    Action<IHttpClientBuilder>? configure = null);

IProtoTargetBuilder AddClient(
    string name,
    Func<ProtoExecutionContext, Uri> baseAddressResolver,
    Action<IHttpClientBuilder>? configure = null);

IProtoTargetBuilder AddClient(
    string name,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
    Action<IHttpClientBuilder>? configure = null);
```

The resolver overloads let the target depend on the running test — useful when each test gets its own tenant or environment. The URI is resolved per request.

A base URL that isn't an absolute `http`/`https` URI throws `ArgumentException`.

### Base URL from configuration

You can leave `baseUrl` out and set it per environment instead:

```json
{
  "ProtoTest": {
    "Clients": {
      "Api": { "BaseUrl": "https://staging.example.test/" }
    }
  }
}
```

An explicitly passed `baseUrl` wins over the configured one.

### No base URL at all

If the target is an in-process ASP.NET Core application, register it with [`AddAspNetCoreServer<TProgram>`](../aspnetcore.md) under the same name instead of giving a base URL.

## Making a request

```csharp
using ProtoTest.Rest;

var response = await Proto.Context.Rest("Api").GetAsync("/api/orders");
```

`Proto.Context.Rest(clientName)` resolves the client by name. Omit the name and it uses, in order: the `[RestClient("…")]` attribute on the test or its class, then `"Default"`.

## What you get automatically

- Every request and response is recorded in [ProtoTrace](../../advanced/prototrace.md).
- An `http.response` observation feeds [coverage collectors](../../advanced/coverage.md).
- With `CaptureAttachments()`, request and response bodies are [attached to the test](./attachments.md) with sensitive values redacted.

## Next

- [Building requests](./requests.md) — the fluent request API and route templating.
- [Responses and assertions](./responses.md) — status and shape assertions.
- [Authentication](./authentication.md) — per-request, per-class and composed authenticators.
- [Attachments and coverage](./attachments.md) — what gets captured and how to tune it.
