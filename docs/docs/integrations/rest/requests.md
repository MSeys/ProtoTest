---
sidebar_position: 2
title: Building requests
description: "Build a REST request fluently with route parameters, body, headers and authentication, and send it with a verb method."
---

# Building requests

`Proto.Context.Rest(name)` returns a `RestRequestBuilder`. Configure it fluently, then finish with a verb method.

```csharp
using var response = await Proto.Context.Rest()
    .Header("X-Correlation-Id", correlationId)
    .Body(new CreateOrderRequest("observability-seat", 12, 19.95m))
    .PostAsync("/api/orders");
```

## Verbs

| Verb | Sends |
| --- | --- |
| `GetAsync(route, params?, ct?)` | `GET` |
| `PostAsync(route, params?, ct?)` | `POST` |
| `PutAsync(route, params?, ct?)` | `PUT` |
| `PatchAsync(route, params?, ct?)` | `PATCH` |
| `DeleteAsync(route, params?, ct?)` | `DELETE` |
| `HeadAsync(route, params?, ct?)` | `HEAD` |
| `OptionsAsync(route, params?, ct?)` | `OPTIONS` |
| `SendAsync(method, route, params?, ct?)` | any `HttpMethod`, for verbs with no helper |

Each returns `Task<RestResponse>` and takes the same route template and optional route-and-query object.

`RestResponse` is `IDisposable`, so use `using var`. The response body is already buffered, so disposing does not cut anything short.

## Route templates and parameters

The second argument fills `{placeholders}` in the route. Whatever is left over becomes the query string.

```csharp
await Proto.Context.Rest().GetAsync(
    "/api/workspaces/{workspaceId}/releases",
    new { workspaceId = workspace.Id, state = "deployed", page = 2 });

// → /api/workspaces/42/releases?state=deployed&page=2
```

The rules:

| Input | Becomes |
| --- | --- |
| `{placeholder}` with a matching value | a path segment, matched case-insensitively (`{workspaceId}=42` → `/workspaces/42`) |
| `{placeholder}` with no value, or a null one | `ArgumentException` naming the parameter |
| leftover non-null property | a query parameter (`state="deployed"` → `?state=deployed`) |
| null property, or an empty collection | dropped (nothing emitted) |
| collection value | a repeated key (`tags=[a,b]` → `tags=a&tags=b`) |
| `bool`, date or time value | invariant text (`true`, round-trip `"O"` dates) |
| `#fragment` in the template | preserved, re-appended after the query string |
| absolute URL as the template | used as-is, bypassing the client's base address |
| non-HTTP(S) scheme, or a relative route with no base address | `InvalidOperationException` |
| colon in the first segment (`orders:search`) | a relative path, as RFC 3986 requires |

Keys and values are escaped with `Uri.EscapeDataString`. A dictionary with string keys works in place of the object.

A per-test base-address resolver wins over the client's `HttpClient.BaseAddress` at request time.

## Bodies

```csharp
RestRequestBuilder Body(object payload, JsonSerializerOptions? options = null);   // JSON
RestRequestBuilder Body(string rawContent, string mediaType = "text/plain");
RestRequestBuilder Body(ReadOnlyMemory<byte> content, string mediaType = "application/octet-stream");
RestRequestBuilder Body(Func<HttpContent> contentFactory);
```

The object overload serialises as `application/json`. The factory overload is invoked per send, so a re-sent request gets fresh content.

## Headers

```csharp
RestRequestBuilder Header(string name, string value);
```

Header names are case-insensitive and the last value for a name wins. ProtoTest tries the request headers first and falls back to the content headers, throwing `InvalidOperationException` if neither accepts it. The trace records header names and the count, not header values.

## Authentication

```csharp
RestRequestBuilder Auth(IProtoHttpAuthenticator authenticator);
RestRequestBuilder Auth<TAuthenticator>(params object[] constructorArgs);
RestRequestBuilder WithoutAuth();
```

See [Authentication](./authentication.md) for how these interact with `[Auth<T>]`.

## Response size limit

Responses are buffered with a cap of 10 MiB by default. A known `Content-Length` above the cap fails before the body is read. Otherwise the buffer stops mid-read. Either way the failure is `ProtoResponseTooLargeException`, whose `MaximumBytes` and `ObservedBytes` tell you the configured cap and the observed size. `HEAD`, `204`, `304` and `1xx` responses never carry a body, so their headers are not measured against the cap. Change it in code or configuration:

```csharp
builder.AddApplication("Api", app => app.AddRest(rest => rest
    .ConfigureResponses(options => options.MaxResponseBodyBytes = 32 * 1024 * 1024)
    .AddClient("Api")));
```

```json
{ "ProtoTest": { "Rest": { "Responses": { "MaxResponseBodyBytes": 33554432 } } } }
```

`MaxDiagnosticBodyLength` (64 KiB) bounds the response body embedded in a status-assertion failure message and in captured attachments. See [Attachments](./attachments.md#options) for the capture options.
