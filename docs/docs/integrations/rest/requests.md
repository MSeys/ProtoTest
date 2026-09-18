---
sidebar_position: 2
title: Building requests
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

```csharp
Task<RestResponse> GetAsync(string routeTemplate, object? routeAndQueryParams = null, CancellationToken ct = default);
Task<RestResponse> PostAsync(...);
Task<RestResponse> PutAsync(...);
Task<RestResponse> PatchAsync(...);
Task<RestResponse> DeleteAsync(...);
Task<RestResponse> HeadAsync(...);
Task<RestResponse> OptionsAsync(...);
Task<RestResponse> SendAsync(HttpMethod method, string routeTemplate, object? routeAndQueryParams = null, CancellationToken ct = default);
```

`RestResponse` is `IDisposable` — use `using var` (the response body is already buffered, so disposing doesn't cut anything short).

## Route templates and parameters

The second argument fills `{placeholders}` in the route; whatever is left over becomes the query string.

```csharp
await Proto.Context.Rest().GetAsync(
    "/api/workspaces/{workspaceId}/releases",
    new { workspaceId = workspace.Id, state = "deployed", page = 2 });

// → /api/workspaces/42/releases?state=deployed&page=2
```

The rules, precisely:

- Placeholders match `{name}` and are resolved **case-insensitively** against the object's public properties. A dictionary with string keys works too.
- A placeholder with no matching value — or a null one — throws `ArgumentException` naming the parameter.
- Every remaining non-null property becomes a query parameter.
- Keys and values are escaped with `Uri.EscapeDataString`, so `"c# & .net"` is safe to pass.
- Nulls are skipped entirely.
- Collections expand to repeated keys: `new { tags = new[] { "csharp", "dotnet" } }` → `tags=csharp&tags=dotnet`. An empty collection emits nothing.
- Values format with invariant culture. `bool` becomes `true`/`false`; dates and times use round-trip (`"O"`) format.
- A `#fragment` in the template is preserved and re-appended after the query string.
- An absolute URL as the template bypasses the client's base address. A non-HTTP(S) scheme, or a relative route with no base address, throws `InvalidOperationException`.

## Bodies

```csharp
RestRequestBuilder Body(object payload, JsonSerializerOptions? options = null);   // JSON
RestRequestBuilder Body(string rawContent, string mediaType = "text/plain");
RestRequestBuilder Body(ReadOnlyMemory<byte> content, string mediaType = "application/octet-stream");
RestRequestBuilder Body(Func<HttpContent> contentFactory);
```

The object overload serialises as `application/json`. The factory overload is invoked per send, so a retried or re-sent request gets fresh content.

## Headers

```csharp
RestRequestBuilder Header(string name, string value);
```

Header names are case-insensitive and the last value for a name wins. ProtoTest tries the request headers first and falls back to the content headers, throwing `InvalidOperationException` if neither accepts it.

## Authentication

```csharp
RestRequestBuilder Auth(IProtoHttpAuthenticator authenticator);
RestRequestBuilder Auth<TAuthenticator>(params object[] constructorArgs);
RestRequestBuilder WithoutAuth();
```

See [Authentication](./authentication.md) for how these interact with `[Auth<T>]`.

## Response size limit

Responses are buffered with a cap of 10 MiB by default. Exceeding it throws `ProtoResponseTooLargeException`. Change it in code or configuration:

```csharp
builder.AddApplication("Api", app => app.AddRest(rest => rest
    .ConfigureResponses(options => options.MaxResponseBodyBytes = 32 * 1024 * 1024)
    .AddClient("Api")));
```

```json
{ "ProtoTest": { "Rest": { "Responses": { "MaxResponseBodyBytes": 33554432 } } } }
```
