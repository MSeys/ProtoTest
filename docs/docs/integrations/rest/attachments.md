---
sidebar_position: 5
title: Attachments and coverage
---

# Attachments and coverage

## Capturing requests and responses

Turn capture on when registering REST:

```csharp
builder.AddApplication("Api", app => app
    .AddRest(rest => rest
        .CaptureAttachments()
        .AddClient("Api")));
```

`CaptureAttachments` returns the REST builder while `AddClient` returns the client's *target* builder, so call it first — or in its own statement when you want to tune it:

```csharp
builder.AddApplication("Api", app => app.AddRest(rest =>
{
    rest.CaptureAttachments(options => options.CaptureExpectedShapes = false);
    rest.AddClient("Api");
}));
```

Without `CaptureAttachments()`, nothing is attached — requests are still traced and observed.

Each test then gets numbered attachments, which your [runner](../../runners/overview.md) shows alongside the result and which are bundled into the [ProtoTrace archive](../../advanced/prototrace.md):

| Attachment | Contains |
| --- | --- |
| `rest-01-request` | the request body |
| `rest-01-response` | the response, with its status in the description |
| `rest-01-expected-shape` | the shape you asserted (`-02`, `-03`… for repeated assertions on one response) |

The number is a per-test sequence, so the second request in a test is `rest-02-…`.

### Options

| Option | Default |
| --- | --- |
| `CaptureRequestBodies` | `true` |
| `CaptureResponses` | `true` |
| `CaptureExpectedShapes` | `true` |
| `RedactSensitiveData` | `true` |
| `MaxDiagnosticBodyLength` | `65536` |
| `SensitiveHeaders` | `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` |
| `SensitiveQueryParameters` | `access_token`, `refresh_token`, `token`, `apiKey`, `api_key`, `key` |
| `SensitiveJsonProperties` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key` |

All of these can also come from configuration:

```json
{
  "ProtoTest": {
    "Rest": {
      "Attachments": {
        "CaptureResponses": false,
        "MaxDiagnosticBodyLength": 16384
      }
    }
  }
}
```

Configuration wins over the `CaptureAttachments(...)` callback — see [Configuration](../../getting-started/configuration.md).

### Redaction

With `RedactSensitiveData` on (the default):

- Sensitive **headers** are replaced with `[REDACTED]` (matched case-insensitively).
- Sensitive **query parameter values** in URLs are replaced with `[REDACTED]`.
- Sensitive **JSON properties** in bodies are redacted, and bodies are truncated to `MaxDiagnosticBodyLength`.

The same sanitiser is used for the response body included in `RestStatusAssertionException` messages.

## Coverage

REST emits an `http.response` observation for every response, identified by method and route template — `GET /api/orders/{id}`, not the concrete URL. Attach a collector to a client to turn those into report items:

```csharp
builder.AddApplication("Api", app => app
    .AddRest(rest => rest
        .AddClient("Api")
        .AddCollector<RestCoverageCollector>()));
```

`RestCoverageCollector` reports every endpoint your suite **called**, with a hit count. It can only list what it saw — to find endpoints you **never** called, and response fields you never asserted, use [`OpenApiCoverageCollector`](../openapi.md), which walks your whole specification.

Both write into the same reports; see [Coverage](../../advanced/coverage.md).
