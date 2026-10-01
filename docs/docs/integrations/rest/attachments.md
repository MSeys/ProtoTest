---
sidebar_position: 5
title: Attachments and coverage
description: "Capture REST requests and responses as attachments, and record which endpoints and properties your suite asserted."
---

# Attachments and coverage

This page shows how to keep each REST request and response next to the test result, how secrets are redacted
from them, and how to report which endpoints your suite called.

## Capturing requests and responses

Turn capture on when registering REST:

```csharp
builder.AddApplication("Api", app => app
    .AddRest(rest => rest
        .CaptureAttachments()
        .AddClient("Api")));
```

`CaptureAttachments` returns the REST builder, while `AddClient` returns the client's *target* builder. Call it first, or in its own statement when you want to tune it:

```csharp
builder.AddApplication("Api", app => app.AddRest(rest =>
{
    rest.CaptureAttachments(options => options.CaptureExpectedShapes = false);
    rest.AddClient("Api");
}));
```

Without `CaptureAttachments()`, nothing is attached. Requests are still traced and observed.

Each test then gets numbered attachments. Your [runner](../../runners/overview.md) shows them alongside the result, and the [ProtoTrace archive](../../observability/prototrace.md) bundles them:

| Attachment | Contains |
| --- | --- |
| `rest-01-request` | the request body, when `CaptureRequestBodies` is on |
| `rest-01-response` | the response, with its status in the description, when `CaptureResponses` is on |
| `rest-01-expected-shape` | the shape you asserted (`-02`, `-03`… for repeated assertions on one response), when `CaptureExpectedShapes` is on |

The number is a per-test sequence, so the second request in a test is `rest-02-…`.

## Options

`ProtoTest:Rest:Attachments` binds a `ProtoHttpAttachmentOptions`. Rest and GraphQL share the same attachment options under their own keys, so capture and redaction match.

| Option | Type | Default |
| --- | --- | --- |
| `CaptureRequestBodies` | `bool` | `true` |
| `CaptureResponses` | `bool` | `true` |
| `CaptureExpectedShapes` | `bool` | `true` |
| `SensitiveHeaders` | `List<string>` | `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` |
| `SensitiveQueryParameters` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key`, `authorization`, `cookie`, `connectionString`, `clientSecret`, `client_secret`, `id_token`, `key` |
| `RedactSensitiveData` (inherited) | `bool` | `true` |
| `MaxDiagnosticBodyLength` (inherited) | `int` | `65536` |
| `SensitiveJsonProperties` (inherited) | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key`, `authorization`, `cookie`, `connectionString`, `clientSecret`, `client_secret`, `id_token` |

`RedactSensitiveData`, `MaxDiagnosticBodyLength` and `SensitiveJsonProperties` come from `JsonDiagnosticOptions`, shared with GraphQL, gRPC and messaging diagnostics.

Each `CaptureAttachments` callback runs in order. Configuration binds last, so it wins over code. Repeated calls each apply.

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

See [Configuration](../../getting-started/configuration.md) for how sections bind.

### Redaction

With `RedactSensitiveData` on (the default):

- Sensitive **headers** are replaced with `[REDACTED]`. Names match case-insensitively, and repeated values are joined with `, `.
- Sensitive **query parameter values** in URLs are replaced with `[REDACTED]`. URI user-info (`user:password@`) is **always** removed, even when redaction is off.
- Sensitive **JSON properties** in bodies are redacted by name, at any depth, tolerating duplicate keys.
- Bodies that are not JSON are scanned for the same keys. This covers form-urlencoded, multipart (`Content-Disposition` `name=`) and XML (element text of sensitive tags and attributes).
- A body that starts like JSON but does not parse is redacted by a text fallback. It covers quoted string values only, so numbers, booleans and nulls in such a body pass through.
- Every body is truncated at `MaxDiagnosticBodyLength` with a `… [N characters truncated]` marker.

The same sanitizer is used for the response body included in `RestStatusAssertionException` messages and for captured attachment content. GraphQL also redacts inline literals in documents, as [Queries and mutations](../graphql/operations.md#transport-details) describes.

## Coverage

REST emits an `http.response` observation for every response, identified by method and route template: `GET /api/orders/{id}`, not the concrete URL. Attach a collector to a client to turn those into report items:

```csharp
builder.AddApplication("Api", app => app
    .AddRest(rest => rest
        .AddClient("Api")
        .AddCollector<RestCoverageCollector>()));
```

`RestCoverageCollector` reports every endpoint your suite **called**, with a hit count, under the `REST` category. It can only list what it saw. To find endpoints you **never** called, and response fields you never asserted, use [`OpenApiCoverageCollector`](../openapi.md), which walks your whole specification and consumes the `http.contract.shape` observations produced by shape assertions.

Both write into the same reports, described in [Coverage](../../observability/coverage.md).
