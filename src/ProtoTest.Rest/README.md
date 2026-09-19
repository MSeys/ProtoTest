# ProtoTest.Rest

A per-test HTTP client with route and query expansion, the shared `[Auth<T>]` pipeline, JSON shape assertions, capture and endpoint coverage.

```bash
dotnet add package ProtoTest.Rest
```

## Quick start

```csharp
builder.AddApplication("Api", app => app.AddRest(rest => rest
    .AddClient("Orders")));   // joins the application's BaseUrl with Endpoints:Orders

[Application("Api", "Rest:Orders")]
[Auth<BearerTokenAuthenticator>("token")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task GetOrder()
    {
        using var response = await Proto.Context.Rest()
            .GetAsync("/orders/{id}", new { id = 42 });

        response.Should.HaveHttpStatus(HttpStatusCode.OK)
            .ShouldMatchShape(new
            {
                id = 42,
                status = JsonValue.NotNull()
            });
    }
}
```

## What it adds

- **Clients** — `Proto.Context.Rest(name?)` resolves a named `HttpClient`; `AddClient` accepts an explicit base URL, an endpoint name or a per-test `Func<ProtoExecutionContext, Uri>` resolver, and `AddClientFrom` reuses another integration's transport.
- **Requests** — `Header`, `Body` (object, raw, bytes or a content factory) and the verb helpers; `{route}` tokens and leftover properties expand into the route and query string.
- **Assertions** — `response.Should.HaveHttpStatus(...)`, `ShouldNot.HaveHttpStatus(...)` and `ShouldMatchShape(...)`; failures carry `RestStatusAssertionException` or `JsonShapeMismatchException`.
- **Authentication** — the shared `[Auth<T>]` pipeline plus per-request `Auth(...)`/`WithoutAuth()`, with built-in bearer, basic and API-key authenticators.
- **Capture and coverage** — `CaptureAttachments()` records redacted request/response bodies; `RestCoverageCollector` reports endpoints from `http.response` observations.
- **Tracing** — `http.request` operations with request/response sections, `http.response`/`http.failure` observations and `assert.http.status`/`assert.json.shape` checks.

## Configuration

Under `ProtoTest:Rest:Responses` and `ProtoTest:Rest:Attachments`; code callbacks (`ConfigureResponses`, `CaptureAttachments`) run first and configuration is bound over them.

| Key | Type | Default |
| --- | --- | --- |
| `MaxResponseBodyBytes` | `int` | `10485760` (10 MiB) |
| `MaxDiagnosticBodyLength` | `int` | `65536` |
| `CaptureRequestBodies` | `bool` | `true` |
| `CaptureResponses` | `bool` | `true` |
| `CaptureExpectedShapes` | `bool` | `true` |
| `RedactSensitiveData` | `bool` | `true` |
| `SensitiveHeaders` | `List<string>` | `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` |
| `SensitiveQueryParameters` | `List<string>` | `access_token`, `refresh_token`, `token`, `apiKey`, `api_key`, `key` |
| `SensitiveJsonProperties` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key` |

Shape assertions are positive-only; response bodies are always buffered in full and there is no retry policy.

## Learn more

- [REST guide](https://prototest.dev/docs/integrations/rest/)
- [DeliveryJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/DeliveryJourney.cs)
