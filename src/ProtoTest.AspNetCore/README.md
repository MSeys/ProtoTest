# ProtoTest.AspNetCore

Hosts an ASP.NET Core application in-process through `WebApplicationFactory<TProgram>` and registers its HTTP client and services on the execution context.

```bash
dotnet add package ProtoTest.AspNetCore
```

## Quick start

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>(configureWebHost: webHost => webHost
        .UseSetting("ProtoTest:TestSupport", "true"))
    .AddRest(rest => rest.AddClient("Api")));

[Application("Api")]
public sealed class ApiTests
{
    [ProtoTest]
    public async Task Orders_are_served_in_process()
    {
        var client = Proto.Context.Client<HttpClient>("Api");
        using var response = await client.GetAsync("/api/orders");
        response.EnsureSuccessStatusCode();

        var services = Proto.Context.ApplicationServices<Program>();
        var store = services.GetRequiredService<IOrderStore>();
    }
}
```

## What it adds

- **Server** — `AddAspNetCoreServer<TProgram>(name = "Default", configureWebHost, configureClientOptions, lifetime)` on the host builder or under an application; `AspNetCoreServerLifetime.PerRun` (default) shares one instance, `PerTest` starts one per test.
- **Accessors** — `Proto.Context.ServerFactory<TProgram>()`, `CreateServerScope<TProgram>()`, `ApplicationServices<TProgram>()` and `ServerService<TProgram, TService>()`.
- **Client transport** — HTTP protocols with no base URL reuse the in-process transport; `traceparent` is injected when an `Activity` is current.
- **Page inventory** — concrete GET page endpoints are observed as `web.page.available`; filter them with `ProtoTest:Applications:{app}:Web:Pages:Include`/`Exclude`.
- **Tracing** — the `aspnetcore.server.initialize` event plus a `server:{TProgram}` entity with lifetime and reuse state.

`PerRun` shares application state across tests (isolation must be data-level); the host overload registers one server per name, and the inventory covers only concrete, method-declared GET page endpoints, recorded once per run.

## Learn more

- [ASP.NET Core guide](https://prototest.dev/docs/integrations/aspnetcore)
- [Demo application registration](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
