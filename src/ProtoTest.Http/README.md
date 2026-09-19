# ProtoTest.Http

Shared HTTP client plumbing and the `[Auth<T>]` model behind REST, GraphQL and gRPC — an extension point for protocol authors, normally transitive.

```bash
dotnet add package ProtoTest.Http
```

Application tests reference `ProtoTest.Rest` or `ProtoTest.GraphQL`, which bring this package in; build directly on it only when writing an HTTP-based protocol integration.

## Quick start

```csharp
// A protocol package registers its keyed options once, under its own section.
ProtoHttpOptionsRegistration.ConfigureResponseOptions(
    services,
    protocolName: "Widgets",
    configurationSectionName: "ProtoTest:Widgets:Responses",
    options => options.MaxResponseBodyBytes = 5 * 1024 * 1024);

// The running test resolves them through the shared execution context.
var options = context.ResolveResponseOptions("Widgets");

// Named clients are created through IHttpClientFactory and traced as client entities.
services.AddSingleton<IProtoClientInitializer<HttpClient>>(
    new ProtoHttpClientInitializer("Widgets", "Default", explicitBaseUrl: "https://widgets.example.test"));

var resolution = ProtoHttpClientResolver.Resolve(context, "Widgets");
```

## What it adds

- **Client initialization** — `ProtoHttpClientInitializer` resolves an address from the explicit argument, `ProtoTest:Applications:{application}:BaseUrl` (joined with `Endpoints:{name}`), or the application's in-process transport.
- **Response options** — `ProtoHttpResponseOptions` (`MaxResponseBodyBytes` 10 MiB, `MaxDiagnosticBodyLength` 64 KiB); register with `TryAddResponseOptions`/`ConfigureResponseOptions` and read with `ResolveResponseOptions`.
- **Attachment options** — `ProtoHttpAttachmentOptions` (capture flags, redaction and sensitive header/query/JSON lists); read with `ResolveAttachmentOptions`, which returns `null` when the protocol never opted in.
- **Buffering** — `ProtoHttpResponseBuffer.BufferAsync` enforces the limit and throws `ProtoResponseTooLargeException`; the content is replaced with a readable buffered copy.
- **Authentication** — `IProtoHttpAuthenticator`, `[Auth<T>]`, `BearerTokenAuthenticator`, `BasicAuthAuthenticator`, `ApiKeyAuthenticator` and `ProtoCompositeHttpAuthenticator`.
- **Diagnostics** — `ProtoHttpDiagnosticSanitizer` redacts JSON/form/multipart/XML bodies, headers and URIs; header values are never traced.

Responses are buffered in full in memory and the package has no retry policy; `TryAddResponseOptions` is first-wins.

## Learn more

- [Integrations overview](https://prototest.dev/docs/integrations/overview)
- [HTTP diagnostics tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Http.Tests/ProtoHttpDiagnosticSanitizerTests.cs)
