---
sidebar_position: 7
title: ASP.NET Core
description: "Run your ASP.NET Core application in-process with WebApplicationFactory and hand its HttpClient to REST and GraphQL: no deployment, no ports."
---

# ASP.NET Core

## What it adds

`ProtoTest.AspNetCore` runs your ASP.NET Core application in-process with `WebApplicationFactory`, and hands its `HttpClient` to the REST and GraphQL clients. No deployed environment, no ports.

The in-memory test host has no address a browser can open; [Hosting a browser journey](#hosting-a-browser-journey) puts the same application on a real loopback port or in its own container when a test needs one.

## Install

```bash
dotnet add package ProtoTest.AspNetCore
dotnet add package ProtoTest.Testcontainers   # the application's image as a container
```

`ProtoTest.AspNetCore` targets `net8.0`, `net9.0` and `net10.0`; the project templates default to `net10.0`, so pass `-f net8.0` or `-f net9.0` when a suite targets an older baseline.

## Compose

Back an application with the in-process server. The application's REST and GraphQL clients reuse its transport, so no network connection is opened:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddRest(rest => rest.AddClient("Api")));
```

The common case has a one-liner: `ProtoTestHost.For<Program>` registers the application in-process under the default name `Api`, and the optional callback composes its protocols on the same call.

```csharp
protected override void Configure(IProtoHostBuilder builder) => ProtoTestHost.For<Program>(builder);

// or with the application's clients declared in the same line:
protected override void Configure(IProtoHostBuilder builder) => ProtoTestHost.For<Program>(
    builder,
    configure: app => app.AddRest(rest => rest.AddClient()));
```

`ProtoTestHost.For` composes exactly `AddApplication("Api", app => app.AddAspNetCoreServer<Program>())`; it adds no behavior of its own, so the sections below apply unchanged. An unnamed `Proto.Context.Rest()` in a suite without a REST client still reaches the application through its in-process transport; register the client when the test needs REST options, attachments or the `REST` capability.

```csharp
public static IProtoApplicationBuilder AddAspNetCoreServer<TProgram>(
    this IProtoApplicationBuilder application,
    Action<IWebHostBuilder>? configureWebHost = null,
    Action<WebApplicationFactoryClientOptions>? configureClientOptions = null,
    AspNetCoreServerLifetime lifetime = AspNetCoreServerLifetime.PerRun)
    where TProgram : class;
```

The server is registered under the **application name**. `TProgram` is your application's entry point; for minimal APIs, add `public partial class Program;` to the application so the test project can see it.

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

Both overloads register a `server` capability named `ASP.NET Core` and expose the application's client under the server name: `"{name}:Factory"` for the `WebApplicationFactory<TProgram>` and `{name}` for the `HttpClient`. Registration is per server name: the host overload keeps the first registration for a name (the same `TProgram` under one name is a no-op, a different one throws naming both programs), while the application overload has no whole-call guard and lets the client initializer pick the first server that initializes. The application overload also registers the application's default transport, so an HTTP client with no configured base URL reuses this server. When the environment configures the application's address, both the server and its capability step aside; see [Real server or in-process?](#real-server-or-in-process).

### One application, or one per test

Starting an ASP.NET Core application takes time, so by default **one instance serves the whole run**:

| `lifetime` | |
| --- | --- |
| `PerRun` *(default)* | The application starts on first use and is disposed when the host is. Tests share its state, so isolate data per test; the sample suite does this with a tenant per test id. |
| `PerTest` | Each test starts its own instance and disposes it afterwards. Nothing is shared, and every test pays the startup cost. |

```csharp
builder.AddApplication("Api", app => app.AddAspNetCoreServer<Program>(lifetime: AspNetCoreServerLifetime.PerTest));
```

Choose `PerTest` when the application keeps state you can't partition (static caches, a single in-memory database without tenant separation), or when tests leave state behind in singleton services, such as a recording fake.

### Options and keys

`ProtoTest.AspNetCore` has no options type of its own. Configuration that affects it:

| Key | Type | Default | |
| --- | --- | --- | --- |
| `ProtoTest:Applications:{app}:BaseUrl` | string | unset → the in-process transport | set it to point the application at a real deployment |
| `ProtoTest:Applications:{app}:Web:Pages:Include` | scalar string or array of globs | empty | keep only matching page paths |
| `ProtoTest:Applications:{app}:Web:Pages:Exclude` | scalar string or array of globs | empty | drop matching page paths |
| `ProtoTest:TestSupport` | `"1"` / `"true"` | unset → the surface is absent | a [sample-side convention](#the-test-support-convention), not a package API |

The glob syntax is `*` for any run of characters and `?` for exactly one, case-insensitive. Include is applied first, then exclude.

### Context API

```csharp
WebApplicationFactory<TProgram> ServerFactory<TProgram>(this ProtoExecutionContext context, string? name = null)
    where TProgram : class;
IServiceScope CreateServerScope<TProgram>(this ProtoExecutionContext context, string? name = null)
    where TProgram : class;
IServiceProvider ApplicationServices<TProgram>(this ProtoExecutionContext context, string? name = null)
    where TProgram : class;
TService ServerService<TProgram, TService>(this ProtoExecutionContext context, string? name = null)
    where TProgram : class where TService : notnull;
void Override<TService>(this ProtoExecutionContext context, TService instance, string? name = null)
    where TService : class;
void Override<TService>(this ProtoExecutionContext context, Func<TService> factory, string? name = null)
    where TService : class;
void Override<TService, TImplementation>(this ProtoExecutionContext context, string? name = null)
    where TService : class where TImplementation : class, TService;
```

When `name` is omitted, each method targets the application selected for the test, then falls back to `"Default"`. `ServerFactory` returns the registered factory; `CreateServerScope` creates a scope from its container that **you** own and dispose. `ApplicationServices` returns the test's own keyed scope over the application, created on first use and disposed with the test, so scoped domain services (repositories, handlers, a `DbContext`) resolve from it; when no server is registered under the resolved name it throws an `InvalidOperationException` naming the expected `AddAspNetCoreServer<TProgram>("{key}")` call, or the configured address and the missing in-process server when the application's address is configured. `ServerService` is `ApplicationServices(...).GetRequiredService<TService>()`.

```csharp
var emails = Proto.Context.ServerService<Program, IEmailSender>("Api");

using var scope = Proto.Context.CreateServerScope<Program>("Api");
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
```

`ServerFactory<TProgram>()` also gives you the underlying `TestServer`: the GraphQL [WebSocket factory](./graphql/subscriptions.md#supplying-your-own-websocket) uses it to open in-process WebSockets. The per-test scope itself is registered as a resource named `application:services:{name}` of kind `"application"`.

## The tasks

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

The sample suite's full registration, an in-process application sharing its store with the tests, is in [Setup.cs](../../../samples/Northstar.ProtoTest/Setup.cs).

### Going further

#### Customising the application

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

The handler chain mirrors `WebApplicationFactoryClientOptions`: a redirect handler when `AllowAutoRedirect` is set and a cookie container when `HandleCookies` is set, with ProtoTest's context-propagating handler appended after them (see [Tracing](#tracing)). In published mode the same chain wraps a real socket, and an explicitly set `options.BaseAddress` overrides the configured address.

`configureWebHost` customizes the server for every test of the run. For a replacement that applies to one test and resets afterwards, see [Substituting services per test](#substituting-services-per-test).

#### Settings from infrastructure

When run-scoped [infrastructure](../foundation/infrastructure.md) started a dependency, its settings are applied to the web host before your callback, so the in-process application reads the same connection strings and choices the tests do:

1. every `ProtoInfrastructureSettings` key is applied with `webHost.UseSetting(key, value)`,
2. then your `configureWebHost` runs; explicit user configuration wins.

The demo passes a connection string, the database provider and `ProtoTest:TestSupport` this way, so the application and the tests read one set of values.

#### Substituting services per test

Swap a service in the application under test for one test, such as a fake clock gateway or a recording mail sender, in the test body or with an attribute:

```csharp
Proto.Context.Override<IEmailSender>(new RecordingEmailSender());

[ReplaceService<IEmailSender>(typeof(RecordingEmailSender))]
public async Task Order_confirmation_sends_an_email() { ... }
```

Cover the error paths by failing a dependency instead of replacing it; resolving it throws:

```csharp
[FailDependency<IEmailSender>]
public async Task A_failed_mail_service_answers_500() { ... }
```

A substituting test runs against a **dedicated server** built with its substitutions before it starts, under either lifetime: the run's shared server is never reconfigured, so one test's override cannot leak into the next, and parallel tests that substitute differently each get their own server. Substitutions compose: a class-level attribute, a method-level attribute and a body `Override` build one server with their union, and the later registration wins. The replacement registers as a singleton of the dedicated server, and the suite's `configureWebHost` still runs first, so the test's substitution wins over the composed registration.

Omit the server name to target the test's selected application (falling back to `"Default"`), or set it explicitly:

```csharp
[ReplaceService<IEmailSender>(typeof(RecordingEmailSender), Server = "Api")]
Proto.Context.Override<IEmailSender>(new RecordingEmailSender(), "Api");
```

Substitution needs the in-process server: with `Server` set the attribute skips unless that named server is in-process; without it, the attribute gates on the test's selected application (falling back to `"Default"`), so a mixed run that publishes one application and hosts another in-process skips instead of failing when the substitution resolves. A configured `BaseUrl` drops the capability they gate on, and `Override` throws naming the address. Apply the override before the test first resolves application services.

#### Test users

An application that should authorize the test user as its own principal opts in to the shipped app-side authentication:

```csharp
app.AddAspNetCoreServer<Program>(webHost => webHost.AddTestUserAuthentication())
   .AddRest(rest => rest.AddClient("Api"));
```

The handler decodes the `ProtoTest-User` header a test's [`[SignedInAs]`](./rest/authentication.md#built-in-test-user) identity travels in and authenticates the request as that user: the name, a `ClaimTypes.Role` claim per role and the declared claims. It becomes the application's default authentication scheme, so the application's own `[Authorize]`, role checks and policies decide - a `[SignedInAs("alice", "admin")]` test reaches an admin endpoint, a `[SignedInAs("bob", "viewer")]` test gets the application's own `403`. No header means no result: anonymous requests stay anonymous and are challenged as usual.

## In the trace and coverage

When the server runs in-process, starting it also inventories its page-like GET routes: each becomes a `web.page.available` observation with metadata `web.application = {server name}` and `web.page.source = "aspnetcore"`, so the [web coverage report](./web/index.md#page-coverage) can show pages that exist but were never visited. The inventory is recorded once per run, by the first test that initializes the server, and coverage aggregates those observations for the whole run: later tests reuse the server without repeating them. A failed inventory resets the latch and traces `web.page.inventory.failed`; an **empty** discovery also does not latch, so a route added after the first test is still inventoried.

The filter is deliberately conservative:

- Dynamic templates are mapped, not excluded: `/orders/{id}` keeps its parameter as `{id}`, a catch-all becomes `{...}`, and a visited concrete path (`/orders/42`) covers the pattern. This is the same identity the web scanner and Vue discovery produce.
- Only endpoints that explicitly declare GET count; an endpoint with no method metadata is not inventoried.
- Razor Page endpoints count.
- An MVC controller action counts only with HTML evidence: a `text/html` response declared through `[Produces]` / `ProducesResponseType` on the endpoint, controller or action, or a view-result return type unwrapped through `Task<>`/`ValueTask<>` (`ViewResult`, `PartialViewResult`, `ViewResultBase`). A JSON action is not a page.
- Routes under `/api`, `/graphql`, `/odata`, `/swagger`, `/openapi`, `/health`, `/metrics`, `/hubs`, `/.well-known` or `/_` are excluded unless they carry that HTML evidence; an `[ApiController]` action that renders a view is still a page.
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

### Tracing

The server is both an event and a state entity, or a single skipped event when it steps aside:

- event `aspnetcore.server.initialize`, in the setup phase, outcome `Succeeded`, carrying `aspnetcore.application.type`, `aspnetcore.server.lifetime`, `aspnetcore.server.reused`, `aspnetcore.web_host.customized` and `aspnetcore.client.customized`;
- entity id `server:{typeof(TProgram).FullName}:{name}`, kind `server`, name `Server · {typeof(TProgram).Name}`, scope = the test name, change `"initialized"`, with the same attributes as its state;
- when an address is configured, event `aspnetcore.server.skipped`, in the setup phase, outcome `Skipped`, carrying `aspnetcore.mode`, `aspnetcore.address` and `aspnetcore.reason`; no server entity is recorded, because nothing in-process exists.

A substitution records one operation per replaced service, `service.substitute` for an `Override` or `[ReplaceService]` and `service.fail` for a `[FailDependency]`, carrying `service.type`, `service.server` and `service.replacement`, linked to the server entity. The test's dedicated server re-records the initialize event and merges `aspnetcore.server.substituted = true` with `aspnetcore.server.substitutions` (the substituted service types) into the server entity state.

The `HttpClient` is a regular ProtoTest client, so its REST and GraphQL calls are traced by those packages. In addition, `ProtoTestContextPropagation.ApplyTo` propagates the current context: when an `Activity.Current` exists and the request has no `traceparent`, it adds `00-{TraceId}-{SpanId}-{01|00}`, and while a test is active on the flow it adds the test's id under `ProtoTestContextPropagation.TestIdHeader` (`x-prototest-test`); existing headers are left untouched. The in-process HTTP client's handler applies it to every request, and a transport that opens a raw in-process request, such as a WebSocket handshake, applies `ApplyTo(HttpRequest)` itself, so the application's clock filter pushes the connecting test's clock. The helper is the entry point for any in-process transport that wants the same clock parity.

## The /test-support convention

`ProtoTest:TestSupport` is a **sample-application convention**, not a package API or configuration key of `ProtoTest.AspNetCore`. The sample application maps its scenario-provisioning surface only when the flag is `1` or `true`; `GET /test-support` then answers 200, and answers 404 otherwise. The sample's testing layer verifies it once per run and fails with a message naming `ProtoTest:TestSupport=true` when the route is missing. The in-process demo enables it with `webHost.UseSetting("ProtoTest:TestSupport", "true")`, and the standalone process with the `ProtoTest__TestSupport` environment variable. An application that never maps the route simply ignores the flag.

## Real server or in-process?

An application's HTTP clients reuse its in-process server when no address resolves for the application, and switch to a real process when one does:

| An address resolves for `ProtoTest:Applications:Api`? | Result |
| --- | --- |
| yes - configured, or published by a started infrastructure piece | real server at that address |
| no | the application's in-process server |

The same suite runs in-process on a developer machine and against a deployed environment in CI, just by setting `BaseUrl` there. With the address **configured**, `AddAspNetCoreServer` **steps aside**: no test server starts, the application's HTTP clients talk to the configured address, and the device clients resolve it the same way. The `ASP.NET Core` capability is dropped with the server, so `[RequiresInProcess]` tests skip instead of failing, and `ServerFactory`/`ApplicationServices` throw naming the address: there is no in-process container to reach.

The step-aside reads **static configuration only**: an address a started piece published does not step the in-process server aside, so `ServerFactory` and the in-process device transport keep working. The HTTP clients do follow the published address (one application-address precedence everywhere), so an application with both a live in-process server and a published address serves its API from the published process. Give the published process **its own application name** when the suite needs both: the demo registers its standalone console as its own application for exactly that reason.

Without an address, the application's HTTP clients fall back to its transport automatically, so there is nothing to point at by hand, and with the default `PerRun` lifetime the server only starts the first time a test actually needs it.

### Hosting a browser journey

A browser can only open a real address, and the in-memory test host has none. Two registrations publish one: the application's own listener hosted in the test process on port 0 (the recipe on [Created through the API, shown in the browser](../recipes/api-then-browser.md#compose) uses this), or the application's own image started as a container.

Host the listener in-process:

```csharp
builder
    .AddLoopbackApplication("Api", App.Create)   // starts Api on http://127.0.0.1:0 and publishes the bound address
    .AddApplication("Api", app => app
        .AddRest(rest => rest.AddClient("Api"))
        .AddWeb());
```

The listener forwards the suite's configuration with the values the infrastructure started before it published as command-line arguments, so a factory that builds from its arguments reads the same addresses the tests do.

Or start the application's image as a container with `ApplicationContainer` from `ProtoTest.Testcontainers`:

```csharp
var api = ApplicationContainer.Container("Api", "my-registry.example.test/orders-api:1.4", port: 8080);
builder
    .AddInfrastructure(
        "OrdersApi",
        chain => chain
            .UseConfigured()      // a configured key skips the container
            .UseContainer(api),
        api.BaseUrlKey)
    .AddHttpReadiness(api.Application, "/health")       // register the probe after the piece that publishes the address
    .AddApplication("Api", app => app
        .AddRest(rest => rest.AddClient("Api"))
        .AddWeb());
```

The piece assumes only the image and the port it listens on: it maps that port to a random host port, publishes `http://{hostname}:{mapped port}` as the application's `BaseUrl`, and its default readiness check waits for the port to accept a connection. The image, the health path and any container build options (an environment, a command, a Testcontainers wait strategy) belong to the suite; `TryStart` reports why the container could not start so a machine without a container runtime can skip before registering it.

Either way the published instance is a real application, not the test host, so it is registered **instead of** `AddAspNetCoreServer` for that application: `ServerFactory`, `ApplicationServices` and `[RequiresInProcess]` need the test host, and the in-process page inventory and the run's clock bridge run only with it. A run that configures `ProtoTest:Applications:Api:BaseUrl` skips both pieces (the key each one fills is satisfied), and the same journey runs against that environment. When a suite needs both the test host and a browser, give the published instance its own application name; the demo registers its standalone console that way.

### Choosing how the application runs

`AddApplication` accepts its providers in priority order, so one composition runs against every mode
without a conditional in `Setup` (see [Environment resolution](../foundation/environment-resolution.md)):

```csharp
builder.AddApplication("Api", app => app
    .UseConfigured()           // ProtoTest:Applications:Api:BaseUrl is set: point at that environment
    .UseInProcess<Program>()   // otherwise: the in-process test server
    .AddRest(rest => rest.AddClient("Api")));
```

`UseInProcess<TProgram>()` is `AddAspNetCoreServer` as a provider: when it wins it declares the
`server` capability - so `[RequiresInProcess]` passes - and the `clock` capability, because the test
clock bridge runs with the server. Put `UseLoopback(App.Create)` in the chain instead of
`AddLoopbackApplication` when the listener is one provider among others: the winner publishes the bound
address, declares no `server` and no `clock` (it is a real process boundary), and a configured provider
earlier in the chain skips the listener entirely. `[RequiresTestClock]` gates tests that advance the
clock against an application the run hosts.

An application that declares no provider registers no chain: `AddAspNetCoreServer` keeps its
step-aside behavior exactly, including the capability drop when `BaseUrl` is configured.

### Clock-dependent tests

`[RequiresTestClock]` skips unless the winner bridges the test clock into the process serving the
application. The in-process server and a hosted worker do; a published, container, AppHost or loopback
application does not, so a journey that boots a device at `2030-01-01` must carry the gate instead of
asserting a time the application never saw.

## Skip

- The registration adds the capability `server` / `ASP.NET Core` with the server name as its instance, so `[RequiresCapability(ProtoCapabilityKinds.Server, CapabilityName = "ASP.NET Core")]` proves composition and `[RequiresServer("Api")]` proves the named instance. `[RequiresInProcess]` is the derived form for tests that need in-process services or transactions; with `BaseUrl` configured the capability is absent and the test skips. See [skip conditions](../foundation/skip-conditions.md).
- `[ReplaceService<T>]` and `[FailDependency<T>]` gate on the capability the substitution resolves: with `Server` set they need that named in-process server; without it they need the server of the test's selected application (falling back to `Default`). With `BaseUrl` configured for that application the test skips instead of substituting a remote one; another live server does not keep the gate open.

## Limits

- **A substituting test builds its own server.** The dedicated instance is released with the test, so per-run sharing and parallel safety hold, but a substituting test under `PerTest` pays two startups (the test's own server, then the substituted one).
- **Replacements are singletons of the dedicated server.** A stateful fake stays per test because the server is per test; a replacement that captures scoped services is the suite's responsibility.
- **A failed dependency throws when resolved.** If the application resolves it while the server starts, the substitution fails the test's setup instead of its requests; prefer failing services the application resolves per request.
- **Closed-box harnesses cannot be substituted.** A containerized or loopback application (`ApplicationContainer`, `AddLoopbackApplication`) and any future out-of-process host expose no service container to the suite: `[ReplaceService]`/`[FailDependency]` skip, and `Override` throws.
- **The app-side test-user authentication replaces the application's default scheme** and exists only on the test host: a suite whose subject is the application's own authentication leaves it unregistered, and the identity's header is then ignored by the application.

- **A startup throw is a setup failure with the application's own exception.** A pipeline build that
  throws - a startup filter or middleware factory - fails the test that starts the server: the
  exception reaches the test unwrapped, the failed start is rolled back (the test's ambient context is
  cleared and no server or client survives it), and the next test starts the application again - the
  fault is retried, not cached. Nothing of the half-started server keeps running.
- **A loopback provider bridges no clock and no substitution.** The hand-built application runs its own
  container, so the run's `TimeProvider` and service replacements cannot reach it: the provider
  declares no `server` and no `clock`, and `[RequiresTestClock]`/`[ReplaceService]` skip for it.
- `PerRun` shares application state across tests; isolate at the data level (the sample uses a tenant per test id). `PerTest` pays the startup cost per test.
- With `BaseUrl` configured there is no in-process server, service container, page inventory or `configureWebHost` callback in play: `configureWebHost` customizes a server that never starts. Tests that need them skip through `[RequiresInProcess]` or a capability condition. A containerized or loopback application is likewise not the test host.
- The host overload allows one registration per server name per builder: repeating a name with the same `TProgram` is a no-op, and registering a different one under it throws instead of silently serving the first program; the application overload has no whole-call guard, and repeated calls can register several initializers, with the first successful one winning.
- `ServerFactory`/`ApplicationServices` require a registration under the resolved name; `ApplicationServices` throws naming the expected call when none is found.
- In-process page inventory covers page-like, explicitly-GET endpoints; parameterized routes keep their `{name}` pattern and catch-alls become `{...}`, so a visited concrete path covers the pattern; API-shaped JSON routes are excluded.
- The inventory is run-level and not repeated after it produces results; a published application never contributes it.

## Links

- [Web overview](./web/index.md) - sessions, options and page coverage.
- [REST clients](./rest/index.md) and [GraphQL clients](./graphql/index.md) - the clients that reuse the in-process transport.
- [Infrastructure](../foundation/infrastructure.md) - how run-scoped settings reach the application.
- [Sample setup](../../../samples/Northstar.ProtoTest/Setup.cs) - the demo's real registration.
