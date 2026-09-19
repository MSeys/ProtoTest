---
sidebar_position: 7
title: ASP.NET Core
description: "Run your ASP.NET Core application in-process with WebApplicationFactory and hand its HttpClient to REST and GraphQL: no deployment, no ports."
---

# ASP.NET Core

`ProtoTest.AspNetCore` runs your ASP.NET Core application in-process with `WebApplicationFactory`, and hands its `HttpClient` to the REST and GraphQL clients. No deployed environment, no ports.

```bash
dotnet add package ProtoTest.AspNetCore
```

## Registering

Back an application with the in-process server. The application's REST and GraphQL clients reuse its transport, so no network connection is opened:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddRest(rest => rest.AddClient("Api")));
```

```csharp
public static IProtoApplicationBuilder AddAspNetCoreServer<TProgram>(
    this IProtoApplicationBuilder application,
    Action<IWebHostBuilder>? configureWebHost = null,
    Action<WebApplicationFactoryClientOptions>? configureClientOptions = null,
    AspNetCoreServerLifetime lifetime = AspNetCoreServerLifetime.PerRun)
    where TProgram : class;
```

The server's client is registered under the **application name**. `TProgram` is your application's entry point — for minimal APIs, add `public partial class Program;` to the application so the test project can see it.

Every test gets its own `HttpClient` from `factory.CreateClient(...)` under that name. The factory is available as `"{name}:Factory"` via `Proto.Context.ServerFactory<TProgram>(name)` — see [reaching into the application](#reaching-into-the-application).

## One application, or one per test

Starting an ASP.NET Core application takes time, so by default **one instance serves the whole run**:

| `lifetime` | |
| --- | --- |
| `PerRun` *(default)* | The application starts on first use and is disposed when the host is. Tests share its state, so isolate data per test — the sample suite does this with a tenant per test id. |
| `PerTest` | Each test starts its own instance and disposes it afterwards. Nothing is shared, and every test pays the startup cost. |

```csharp
builder.AddApplication("Api", app => app.AddAspNetCoreServer<Program>(lifetime: AspNetCoreServerLifetime.PerTest));
```

Choose `PerTest` when the application keeps state you can't partition — static caches, a single in-memory database without tenant separation — or when tests leave state behind in singleton services, such as a recording fake.

The trace's `aspnetcore.server.initialize` event records the lifetime and whether the instance was reused (`aspnetcore.server.reused`).

## Page coverage

When the server runs in-process, starting it also inventories its page-like GET routes: each becomes a `web.page.available` observation, so the [web coverage report](./web/index.md#page-coverage) can show pages that exist but were never visited. The inventory is recorded once, by the first test that initializes the server, and coverage aggregates those observations for the whole run, so later tests do not repeat them; a failed inventory is not recorded, so a later test retries it. Razor Pages, MVC actions with HTML evidence (a `text/html` response or a view-result return type) and endpoints that declare `text/html` count — including `[ApiController]` actions that produce HTML; JSON controller actions do not. Only endpoints that explicitly declare GET count, and API-shaped, parameterized and catch-all routes do not. Narrow or widen the list per application with `ProtoTest:Applications:{app}:Web:Pages:Include` and `:Exclude` globs (an array or a scalar value). A published application never starts in-process, so there the inventory comes from `ProtoTest:Web:Pages` instead.

## Customising the application

Replace services the same way you would with `WebApplicationFactory` directly:

```csharp
builder.AddAspNetCoreServer<Program>(
    "Api",
    configureWebHost: webHost => webHost.ConfigureTestServices(services =>
    {
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender, RecordingEmailSender>();
    }),
    configureClientOptions: options => options.AllowAutoRedirect = false);
```

## Reaching into the application

```csharp
WebApplicationFactory<TProgram> ServerFactory<TProgram>(this ProtoExecutionContext context, string? name = null);
IServiceScope CreateServerScope<TProgram>(this ProtoExecutionContext context, string? name = null);
TService ServerService<TProgram, TService>(this ProtoExecutionContext context, string? name = null);
```

When `name` is omitted, each method targets the application selected for the test, then falls back to `"Default"`.

```csharp
var emails = Proto.Context.ServerService<Program, IEmailSender>("Api");

using var scope = Proto.Context.CreateServerScope<Program>("Api");
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
```

`ServerFactory<TProgram>()` also gives you the underlying `TestServer` — the GraphQL [WebSocket factory](./graphql/subscriptions.md#supplying-your-own-websocket) uses it to open in-process WebSockets.

## Real server or in-process?

An application's HTTP clients reuse its in-process server when no URL is configured, and switch to a deployed environment when one is:

| `ProtoTest:Applications:Api:BaseUrl` set? | Result |
| --- | --- |
| yes | real server at the configured URL |
| no | the application's in-process server |

The same suite runs in-process on a developer machine and against a deployed environment in CI, just by setting `BaseUrl` there. `AddClientFrom(name, sourceClientName, basePath?)` remains available when a client must reuse a *differently named* client's transport or a path prefix.

In the first row the application is never started — with the default `PerRun` lifetime it only starts the first time a test actually needs it.
