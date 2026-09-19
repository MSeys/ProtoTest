---
sidebar_position: 7
title: ASP.NET Core
description: "Run your ASP.NET Core application in-process with WebApplicationFactory and hand its HttpClient to REST and GraphQL: no deployment, no ports."
---

# ASP.NET Core

`ProtoTest.AspNetCore` runs your ASP.NET Core application in-process with `WebApplicationFactory`, and hands its `HttpClient` to the REST and GraphQL clients. No deployed environment, no ports.

```bash
dotnet add package ProtoTest.AspNetCore --prerelease
```

The package targets `net8.0`, `net9.0` and `net10.0`; the project templates default to `net10.0`, so pass `-f net8.0` or `-f net9.0` when a suite targets an older baseline.

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

The server is registered under the **application name**. `TProgram` is your application's entry point — for minimal APIs, add `public partial class Program;` to the application so the test project can see it.

The same registration exists on a host builder, for suites that drive the server directly:

```csharp
public static IProtoHostBuilder AddAspNetCoreServer<TProgram>(
    this IProtoHostBuilder builder,
    string name = "Default",
    Action<IWebHostBuilder>? configureWebHost = null,
    Action<WebApplicationFactoryClientOptions>? configureClientOptions = null,
    AspNetCoreServerLifetime lifetime = AspNetCoreServerLifetime.PerRun)
    where TProgram : class;
```

Both overloads register a `server` capability named `ASP.NET Core` and expose the application's client under the server name — `"{name}:Factory"` for the `WebApplicationFactory<TProgram>` and `{name}` for the `HttpClient`. Registration is per server name: the host overload keeps the first registration for a name and ignores a repeat, while the application overload has no whole-call guard and lets the client initializer pick the first server that initializes. The application overload also registers the application's default transport, so an HTTP client with no configured base URL reuses this server.

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

## Options and keys

`ProtoTest.AspNetCore` has no options type of its own. Configuration that affects it:

| Key | Type | Default | |
| --- | --- | --- | --- |
| `ProtoTest:Applications:{app}:BaseUrl` | string | unset → the in-process transport | set it to point the application at a real deployment |
| `ProtoTest:Applications:{app}:Web:Pages:Include` | scalar string or array of globs | empty | keep only matching page paths |
| `ProtoTest:Applications:{app}:Web:Pages:Exclude` | scalar string or array of globs | empty | drop matching page paths |
| `ProtoTest:TestSupport` | `"1"` / `"true"` | unset → the surface is absent | a [sample-side convention](#the-test-support-convention), not a package API |

The glob syntax is `*` for any run of characters and `?` for exactly one, case-insensitive. Include is applied first, then exclude.

## Context API

```csharp
WebApplicationFactory<TProgram> ServerFactory<TProgram>(this ProtoExecutionContext context, string? name = null)
    where TProgram : class;
IServiceScope CreateServerScope<TProgram>(this ProtoExecutionContext context, string? name = null)
    where TProgram : class;
IServiceProvider ApplicationServices<TProgram>(this ProtoExecutionContext context, string? name = null)
    where TProgram : class;
TService ServerService<TProgram, TService>(this ProtoExecutionContext context, string? name = null)
    where TProgram : class where TService : notnull;
```

When `name` is omitted, each method targets the application selected for the test, then falls back to `"Default"`. `ServerFactory` returns the registered factory; `CreateServerScope` creates a scope from its container that **you** own and dispose. `ApplicationServices` returns the test's own keyed scope over the application — created on first use, disposed with the test — so scoped domain services (repositories, handlers, a `DbContext`) resolve from it; when no server is registered under the resolved name it throws an `InvalidOperationException` naming the expected `AddAspNetCoreServer<TProgram>("{key}")` call. `ServerService` is `ApplicationServices(...).GetRequiredService<TService>()`.

```csharp
var emails = Proto.Context.ServerService<Program, IEmailSender>("Api");

using var scope = Proto.Context.CreateServerScope<Program>("Api");
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
```

`ServerFactory<TProgram>()` also gives you the underlying `TestServer` — the GraphQL [WebSocket factory](./graphql/subscriptions.md#supplying-your-own-websocket) uses it to open in-process WebSockets. The per-test scope itself is registered as a resource named `application:services:{name}` of kind `"application"`.

## Quick start

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddRest(rest => rest.AddClient("Api")));
```

```csharp
[ProtoTest]
public async Task Health_endpoint_answers()
{
    using var response = await Proto.Context.Rest("Api").GetAsync("/health");
    response.Should.HaveHttpStatus(HttpStatusCode.OK);
}
```

The sample suite's full registration — an in-process application sharing its store with the tests — is in [Setup.cs](../../../samples/ProtoTest.Demo/Setup.cs).

## Going further

### Customising the application

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

The handler chain mirrors `WebApplicationFactoryClientOptions`: a redirect handler when `AllowAutoRedirect` is set and a cookie container when `HandleCookies` is set, with ProtoTest's `ProtoTraceContextHandler` appended after them.

### Settings from infrastructure

When run-scoped [infrastructure](../foundation/infrastructure.md) started a dependency, its settings are applied to the web host before your callback, so the in-process application reads the same connection strings and choices the tests do:

1. every `ProtoInfrastructureSettings` key is applied with `webHost.UseSetting(key, value)`,
2. then your `configureWebHost` runs — explicit user configuration wins.

The demo passes a connection string, the database provider and `ProtoTest:TestSupport` this way, so the application and the tests read one set of values.

## Page coverage

When the server runs in-process, starting it also inventories its page-like GET routes: each becomes a `web.page.available` observation with metadata `web.application = {server name}` and `web.page.source = "aspnetcore"`, so the [web coverage report](./web/index.md#page-coverage) can show pages that exist but were never visited. The inventory is recorded once per run, by the first test that initializes the server, and coverage aggregates those observations for the whole run: later tests reuse the server without repeating them. A failed inventory resets the latch and traces `web.page.inventory.failed`; an **empty** discovery also does not latch, so a route added after the first test is still inventoried.

The filter is deliberately conservative:

- Only concrete routes count — a template containing `{` (`/orders/{id}`, catch-alls) is excluded, because a route pattern cannot be matched by a concrete visited path.
- Only endpoints that explicitly declare GET count; an endpoint with no method metadata is not inventoried.
- Razor Page endpoints count.
- An MVC controller action counts only with HTML evidence — a `text/html` response declared through `[Produces]` / `ProducesResponseType` on the endpoint, controller or action, or a view-result return type unwrapped through `Task<>`/`ValueTask<>` (`ViewResult`, `PartialViewResult`, `ViewResultBase`). A JSON action is not a page.
- Routes under `/api`, `/graphql`, `/odata`, `/swagger`, `/openapi`, `/health`, `/metrics`, `/hubs`, `/.well-known` or `/_` are excluded unless they carry that HTML evidence — an `[ApiController]` action that renders a view is still a page.
- Endpoints are de-duplicated by reference across the registered `EndpointDataSource` services and `IEndpointRouteBuilder.DataSources`.

Refine the result with the Include/Exclude globs:

```json
{
  "ProtoTest": {
    "Applications": {
      "Api": {
        "Web": {
          "Pages": {
            "Include": [ "/portal/*" ],
            "Exclude": [ "/portal/legacy/*" ]
          }
        }
      }
    }
  }
}
```

A published application never starts in-process, so its inventory comes from the explicit `ProtoTest:Web:Pages` list, the frontend source folder or Vue discovery instead.

## Tracing

The server is both an event and a state entity:

- event `aspnetcore.server.initialize`, in the setup phase, outcome `Succeeded`, carrying `aspnetcore.application.type`, `aspnetcore.server.lifetime`, `aspnetcore.server.reused`, `aspnetcore.web_host.customized` and `aspnetcore.client.customized`;
- entity id `server:{typeof(TProgram).FullName}`, kind `server`, name `Server · {typeof(TProgram).Name}`, scope = the test name, change `"initialized"`, with the same attributes as its state.

The `HttpClient` is a regular ProtoTest client, so its REST and GraphQL calls are traced by those packages. In addition, `ProtoTraceContextHandler` propagates the current trace context: when an `Activity.Current` exists and the outgoing request has no `traceparent`, the handler adds `00-{TraceId}-{SpanId}-{01|00}`; an existing `traceparent` is left untouched.

## The /test-support convention

`ProtoTest:TestSupport` is a **sample-application convention**, not a package API or configuration key of `ProtoTest.AspNetCore`. The sample application maps its scenario-provisioning surface only when the flag is `1` or `true`; `GET /test-support` then answers 200, and answers 404 otherwise. The sample's testing layer verifies it once per run and fails with a message naming `ProtoTest:TestSupport=true` when the route is missing. The in-process demo enables it with `webHost.UseSetting("ProtoTest:TestSupport", "true")`, and the standalone process with the `ProtoTest__TestSupport` environment variable. An application that never maps the route simply ignores the flag.

## Real server or in-process?

An application's HTTP clients reuse its in-process server when no URL is configured, and switch to a deployed environment when one is:

| `ProtoTest:Applications:Api:BaseUrl` set? | Result |
| --- | --- |
| yes | real server at the configured URL |
| no | the application's in-process server |

The same suite runs in-process on a developer machine and against a deployed environment in CI, just by setting `BaseUrl` there. `AddClientFrom(name, sourceClientName, basePath?)` remains available when a client must reuse a *differently named* client's transport or a path prefix.

In the first row the application is never started — with the default `PerRun` lifetime it only starts the first time a test actually needs it.

## Skip

- The registration adds the capability `server` / `ASP.NET Core`, so `[RequiresCapability(ProtoCapabilityKinds.Server, CapabilityName = "ASP.NET Core")]` proves composition. `[RequiresInProcess]` is the derived form for tests that need in-process services or transactions; it skips when the suite runs against a published environment. See [skip conditions](../foundation/skip-conditions.md).

## Limits

- `PerRun` shares application state across tests; isolate at the data level (the sample uses a tenant per test id). `PerTest` pays the startup cost per test.
- The host overload allows one registration per server name per builder; the application overload has no whole-call guard, and repeated calls can register several initializers, with the first successful one winning.
- `ServerFactory`/`ApplicationServices` require a registration under the resolved name; `ApplicationServices` throws naming the expected call when none is found.
- In-process page inventory is limited to concrete, explicitly-GET, page-like endpoints; parameterized and catch-all routes and API-shaped JSON routes are excluded.
- The inventory is run-level and not repeated after it produces results; a published application never contributes it.

## Links

- [Web overview](./web/index.md) — sessions, options and page coverage.
- [REST clients](./rest/index.md) and [GraphQL clients](./graphql/index.md) — the clients that reuse the in-process transport.
- [Infrastructure](../foundation/infrastructure.md) — how run-scoped settings reach the application.
- [Sample setup](../../../samples/ProtoTest.Demo/Setup.cs) — the demo's real registration.
