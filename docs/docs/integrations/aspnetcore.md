---
sidebar_position: 7
title: ASP.NET Core
description: "Run your ASP.NET Core application in-process with WebApplicationFactory and hand its HttpClient to REST and GraphQL: no deployment, no ports."
---

# ASP.NET Core

`ProtoTest.AspNetCore` runs your ASP.NET Core application in-process with `WebApplicationFactory`, and hands its `HttpClient` to the REST and GraphQL clients. No deployed environment, no ports.

```csharp
[ProtoTest]
public async Task Health_endpoint_answers()
{
    using var response = await Proto.Context.Rest("Api").GetAsync("/health");
    response.Should.HaveHttpStatus(HttpStatusCode.OK);
}
```

Run it with `dotnet test`. A green run prints `Passed Health_endpoint_answers`, and the trace records an `aspnetcore.server.initialize` event with a `server` entity for the application.

## What it adds

The application runs inside the test process. That gives a test what a deployed application cannot:

- **No ports.** The REST and GraphQL clients reuse the server's in-memory transport.
- **The application's services.** A test can resolve a service or a `DbContext` from the application.
- **Per-test substitution.** A test can replace or fail one service, on a server of its own.
- **The test clock.** The application reads each test's clock through `TimeProvider`.
- **Test users.** An opt-in handler signs in the identity a test declares with `[SignedInAs]`.
- **Page inventory.** The server's page-like routes feed the web coverage report.

The in-memory host has no address a browser can open. [Hosting a browser journey](#hosting-a-browser-journey) puts the same application on a real loopback port, or in its own container, when a test needs one.

## Install

```bash
dotnet add package ProtoTest.AspNetCore
dotnet add package ProtoTest.Testcontainers   # the application's image as a container
```

`ProtoTest.AspNetCore` targets `net8.0`, `net9.0` and `net10.0`. The project templates default to `net10.0`, so
pass `--framework net8.0` or `--framework net9.0` for an older baseline.

## Compose

Back an application with the in-process server. Its REST and GraphQL clients reuse the server's transport, so no network connection opens:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddRest(rest => rest.AddClient("Api")));
```

For the common case, `ProtoTestHost.For<Program>` does the same in one line, under the default name `Api`. Its optional callback adds the application's clients:

```csharp
protected override void Configure(IProtoHostBuilder builder) => ProtoTestHost.For<Program>(builder);

// or with the application's clients declared in the same line:
protected override void Configure(IProtoHostBuilder builder) => ProtoTestHost.For<Program>(
    builder,
    configure: app => app.AddRest(rest => rest.AddClient()));
```

`ProtoTestHost.For` is exactly `AddApplication("Api", app => app.AddAspNetCoreServer<Program>())` and adds no
behavior of its own. An unnamed `Proto.Context.Rest()` reaches the application through its in-process transport
even without a REST client. Register the client when a test needs REST options, attachments or the `REST`
capability.

Both registrations, on an application or on the host, declare the `ASP.NET Core` server capability. Both step
aside when the application's address is configured. The [reference](#reference) has the signatures and rules.

### One application, or one per test

Starting an ASP.NET Core application takes time, so by default **one instance serves the whole run**:

| `lifetime` | |
| --- | --- |
| `PerRun` *(default)* | The application starts on first use and is disposed when the host is. Tests share its state, so isolate data per test; the sample suite does this with a tenant per test id. |
| `PerTest` | Each test starts its own instance and disposes it afterwards. Nothing is shared, and every test pays the startup cost. |

```csharp
builder.AddApplication("Api", app => app.AddAspNetCoreServer<Program>(lifetime: AspNetCoreServerLifetime.PerTest));
```

Choose `PerTest` when the application keeps state you cannot split per test, such as static caches or one
in-memory database without tenants. Choose it too when tests leave state in singleton services, such as a
recording fake.

## The tasks

The `Health_endpoint_answers` test above is the whole pattern: compose the server with its client, call it, assert. Beyond that, a suite usually needs one of these:

1. [Change the application for the whole run](#customising-the-application), such as a fake mail sender.
2. [Replace or fail a service for one test](#substituting-services-per-test).
3. [Reach the application's services from a test](#reaching-the-applications-services), such as its `DbContext`.
4. [Sign requests in as test users](#test-users) the application authorizes itself.
5. [Run the same suite against a deployed environment](#real-server-or-in-process).
6. [Give a browser a real address](#hosting-a-browser-journey).

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

`configureWebHost` changes the server for every test in the run. For a change that applies to one test, see
[Substituting services per test](#substituting-services-per-test).

The client's handler chain follows `WebApplicationFactoryClientOptions`: a redirect handler when
`AllowAutoRedirect` is set, and a cookie container when `HandleCookies` is set. ProtoTest's context-propagating
handler comes after them (see [Tracing](#tracing)). Against a published application, the same chain wraps a real
socket, and an explicit `options.BaseAddress` overrides the configured address.

#### Settings from infrastructure

When run-scoped [infrastructure](../foundation/infrastructure.md) started a dependency, the in-process application
gets its settings, so it reads the same connection strings as the tests:

1. every `ProtoInfrastructureSettings` key is applied with `webHost.UseSetting(key, value)`,
2. then your `configureWebHost` runs; explicit user configuration wins.

The sample suite passes a connection string, the database provider and `ProtoTest:TestSupport` this way.

### Substituting services per test

Swap a service in the application for one test, such as a fake clock gateway or a recording mail sender. Do it in the test body or with an attribute:

```csharp
Proto.Context.Override<IEmailSender>(new RecordingEmailSender());

[ReplaceService<IEmailSender>(typeof(RecordingEmailSender))]
public async Task Order_confirmation_sends_an_email() { ... }
```

To cover an error path, fail a dependency instead of replacing it. Resolving it then throws:

```csharp
[FailDependency<IEmailSender>]
public async Task A_failed_mail_service_answers_500() { ... }
```

A substituting test runs against a **dedicated server**, built with its substitutions before it starts, under
either lifetime. The run's shared server is never changed, so one test's override cannot leak into the next, and
parallel tests that substitute differently each get their own server.

Substitutions combine. A class attribute, a method attribute and a body `Override` build one server with all of
them, and the later registration wins. Each replacement is a singleton of the dedicated server. The suite's
`configureWebHost` still runs first, so the test's substitution wins over it.

Without a server name, the substitution targets the test's selected application, or `"Default"`. Name one explicitly:

```csharp
[ReplaceService<IEmailSender>(typeof(RecordingEmailSender), Server = "Api")]
Proto.Context.Override<IEmailSender>(new RecordingEmailSender(), "Api");
```

Substitution needs the in-process server, and the two forms refuse differently:

- The attributes skip unless the target server is in-process. In a mixed run that publishes one application and hosts another, a test aimed at the published one skips instead of failing.
- `Override` throws, naming the configured address, because a configured `BaseUrl` removes the in-process server.

Apply the override before the test first resolves application services.

### Reaching the application's services

A test can resolve the application's own services, for example to check what a fake recorded or to read the
database directly:

```csharp
var emails = Proto.Context.ServerService<Program, IEmailSender>("Api");

using var scope = Proto.Context.CreateServerScope<Program>("Api");
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
```

`ApplicationServices` gives the test its own scope over the application, created on first use and disposed with
the test. Scoped services, such as repositories, handlers or a `DbContext`, resolve from it. `ServerService` is
`ApplicationServices(...).GetRequiredService<TService>()`. `CreateServerScope` creates a scope that **you** own and
dispose. The [Context API](#context-api) lists them all.

### Test users

An application that should authorize the test user as its own principal opts in to the shipped app-side authentication:

```csharp
app.AddAspNetCoreServer<Program>(webHost => webHost.AddTestUserAuthentication())
   .AddRest(rest => rest.AddClient("Api"));
```

The handler reads the `ProtoTest-User` header that carries a test's [`[SignedInAs]`](./rest/authentication.md#built-in-test-user)
identity. It signs the request in as that user: the name, a `ClaimTypes.Role` claim per role, and the declared
claims. It becomes the application's default authentication scheme, so the application's own `[Authorize]`, role
checks and policies decide. A `[SignedInAs("alice", "admin")]` test reaches an admin endpoint, and a
`[SignedInAs("bob", "viewer")]` test gets the application's own `403`. A request without the header stays anonymous
and is challenged as usual.

### Real server or in-process?

An application's HTTP clients use its in-process server when no address resolves for it, and a real process when one does:

| An address resolves for `ProtoTest:Applications:Api`? | Result |
| --- | --- |
| yes - configured, or published by a started infrastructure piece | real server at that address |
| no | the application's in-process server |

So the same suite runs in-process on a developer machine and against a deployed environment in CI: set `BaseUrl`
there. With a **configured** address, `AddAspNetCoreServer` **steps aside**:

- No test server starts. The HTTP clients and the device clients use the configured address.
- The `ASP.NET Core` capability goes with the server, so `[RequiresInProcess]` tests skip instead of failing.
- `ServerFactory` and `ApplicationServices` throw, naming the address: there is no in-process container to reach.

Only **static configuration** makes the server step aside. An address that a started piece published does not, so
`ServerFactory` and the in-process device transport keep working. The HTTP clients do follow the published
address, so an application with both serves its API from the published process. When a suite needs both, give the
published process **its own application name**. The sample suite registers its standalone console that way.

Without an address, the HTTP clients fall back to the in-process transport by themselves. With the default
`PerRun` lifetime, the server starts only when a test first needs it.

### Hosting a browser journey

A browser can only open a real address, and the in-memory test host has none. Two registrations publish one: the
application's own listener hosted in the test process on port 0 (the recipe
[Created through the API, shown in the browser](../recipes/api-then-browser.md#compose) uses this), or the
application's own image started as a container.

Host the listener in-process:

```csharp
builder
    .AddLoopbackApplication("Api", App.Create)   // starts Api on http://127.0.0.1:0 and publishes the bound address
    .AddApplication("Api", app => app
        .AddRest(rest => rest.AddClient("Api"))
        .AddWeb());
```

The listener passes the suite's configuration, plus the values earlier infrastructure published, as command-line
arguments. A factory that builds from its arguments reads the same addresses as the tests.

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

The container piece needs only the image and the port it listens on. It maps that port to a random host port,
publishes `http://{hostname}:{mapped port}` as the application's `BaseUrl`, and by default waits until the port
accepts a connection. The image, the health path and any container options (an environment, a command, a
Testcontainers wait strategy) belong to the suite. `TryStart` reports why the container could not start, so a
machine without a container runtime can skip before registering it.

Either way the published instance is a real application, not the test host. Register it **instead of**
`AddAspNetCoreServer` for that application. `ServerFactory`, `ApplicationServices` and `[RequiresInProcess]` need the
test host, and so do the page inventory and the clock bridge. A run that configures
`ProtoTest:Applications:Api:BaseUrl` skips both pieces, and the same journey runs against that environment. When a
suite needs the test host and a browser, give the published instance its own application name, as the sample
does.

### Choosing how the application runs

`AddApplication` accepts its providers in priority order, so one composition runs in every mode without a
conditional in `Setup` (see [Environment resolution](../foundation/environment-resolution.md)):

```csharp
builder.AddApplication("Api", app => app
    .UseConfigured()           // ProtoTest:Applications:Api:BaseUrl is set: point at that environment
    .UseInProcess<Program>()   // otherwise: the in-process test server
    .AddRest(rest => rest.AddClient("Api")));
```

`UseInProcess<TProgram>()` is `AddAspNetCoreServer` as a provider. When it wins, it declares the `server`
capability, so `[RequiresInProcess]` passes, and the `clock` capability, because the clock bridge runs with the
server.

Put `UseLoopback(App.Create)` in the chain, instead of `AddLoopbackApplication`, when the listener is one provider
among others. When it wins, it publishes the bound address and declares no `server` and no `clock`, because it is a
separate process boundary. A configured provider earlier in the chain skips the listener entirely.

An application that declares no provider has no chain. `AddAspNetCoreServer` then keeps its step-aside behavior
exactly, including dropping the capability when `BaseUrl` is configured.

### Clock-dependent tests

`[RequiresTestClock]` skips unless the winning provider bridges the test clock into the process serving the
application. The in-process server and a hosted worker do. A published, container, AppHost or loopback application
does not. So a journey that boots a device at `2030-01-01` carries the gate, instead of asserting a time the
application never saw.

## Reference

```csharp
public static IProtoApplicationBuilder AddAspNetCoreServer<TProgram>(
    this IProtoApplicationBuilder application,
    Action<IWebHostBuilder>? configureWebHost = null,
    Action<WebApplicationFactoryClientOptions>? configureClientOptions = null,
    AspNetCoreServerLifetime lifetime = AspNetCoreServerLifetime.PerRun)
    where TProgram : class;
```

The server is registered under the **application name**. `TProgram` is your application's entry point. For minimal
APIs, add `public partial class Program;` to the application so the test project can see it.

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

- Both overloads register a `server` capability named `ASP.NET Core`. They expose the application's client under the server name: `"{name}:Factory"` for the `WebApplicationFactory<TProgram>` and `{name}` for the `HttpClient`.
- The host overload keeps the first registration for a name. The same `TProgram` again is a no-op, and a different one throws, naming both programs.
- The application overload has no such guard. Repeated calls register several initializers, and the first that initializes wins.
- The application overload also registers the application's default transport, so an HTTP client without a base URL reuses this server.
- When the environment configures the application's address, the server and its capability step aside. See [Real server or in-process?](#real-server-or-in-process).

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

- Without `name`, each method targets the test's selected application, then `"Default"`.
- `ServerFactory` returns the registered factory. `ServerFactory<TProgram>()` also gives you the underlying `TestServer`, which the GraphQL [WebSocket factory](./graphql/subscriptions.md#supplying-your-own-websocket) uses to open in-process WebSockets.
- `ApplicationServices` throws an `InvalidOperationException` when no server is registered under the resolved name. The message names the expected `AddAspNetCoreServer<TProgram>("{key}")` call, or, when an address is configured, that address and the missing in-process server.
- The per-test scope is registered as a resource named `application:services:{name}`, of kind `"application"`.

The sample suite's full registration, an in-process application sharing its store with the tests, is in [Setup.cs](../../../samples/Northstar.ProtoTest/Setup.cs).

## In the trace and coverage

When the server runs in-process, starting it also lists its page-like GET routes. Each becomes a
`web.page.available` observation, with metadata `web.application = {server name}` and
`web.page.source = "aspnetcore"`. The [web coverage report](./web/index.md#page-coverage) then shows pages that exist
but were never visited.

The first test that starts the server records the list once for the run, and coverage aggregates it for the whole
run. A failed inventory traces `web.page.inventory.failed`, and the next test tries again. An **empty** inventory
also does not count as done, so a route added after the first test is still listed.

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

A published application never starts in-process. Its inventory comes from the explicit `ProtoTest:Web:Pages` list,
the frontend source folder or Vue discovery instead.

### Tracing

The server is both an event and a state entity, or a single skipped event when it steps aside:

- event `aspnetcore.server.initialize`, in the setup phase, outcome `Succeeded`, carrying `aspnetcore.application.type`, `aspnetcore.server.lifetime`, `aspnetcore.server.reused`, `aspnetcore.web_host.customized` and `aspnetcore.client.customized`;
- entity id `server:{typeof(TProgram).FullName}:{name}`, kind `server`, name `Server · {typeof(TProgram).Name}`, scope = the test name, change `"initialized"`, with the same attributes as its state;
- when an address is configured, event `aspnetcore.server.skipped`, in the setup phase, outcome `Skipped`, carrying `aspnetcore.mode`, `aspnetcore.address` and `aspnetcore.reason`; no server entity is recorded, because nothing in-process exists.

A substitution records one operation per replaced service: `service.substitute` for an `Override` or
`[ReplaceService]`, and `service.fail` for a `[FailDependency]`. Each carries `service.type`, `service.server` and
`service.replacement`, and links to the server entity. The test's dedicated server records the initialize event
again, and adds `aspnetcore.server.substituted = true` and `aspnetcore.server.substitutions` (the substituted
service types) to the server entity.

The `HttpClient` is a regular ProtoTest client, so the REST and GraphQL packages trace its calls. On top of that,
`ProtoTestContextPropagation.ApplyTo` passes the current context on with each request:

- When an `Activity.Current` exists and the request has no `traceparent`, it adds `00-{TraceId}-{SpanId}-{01|00}`.
- While a test runs on the flow, it adds the test's id under `ProtoTestContextPropagation.TestIdHeader` (`x-prototest-test`).
- Existing headers are left untouched.

The in-process client's handler applies it to every request. A transport that opens a raw in-process request, such
as a WebSocket handshake, calls `ApplyTo(HttpRequest)` itself, so the application's clock filter uses the
connecting test's clock. Any in-process transport that needs the same clock behavior uses this helper.

## The /test-support convention

`ProtoTest:TestSupport` is a **convention of the sample application**, not a package API or a configuration key of
`ProtoTest.AspNetCore`. The sample maps its scenario-provisioning routes only when the flag is `1` or `true`:
`GET /test-support` then answers 200, and 404 otherwise. The sample's testing layer checks it once per run, and
fails with a message naming `ProtoTest:TestSupport=true` when the route is missing. The in-process sample sets it
with `webHost.UseSetting("ProtoTest:TestSupport", "true")`, and the standalone process with the
`ProtoTest__TestSupport` environment variable. An application that never maps the route ignores the flag.

## Skip

- The registration adds the capability `server` / `ASP.NET Core`, with the server name as its instance. `[RequiresCapability(ProtoCapabilityKinds.Server, CapabilityName = "ASP.NET Core")]` proves the composition, and `[RequiresServer("Api")]` proves the named instance. `[RequiresInProcess]` is the form for tests that need in-process services or transactions: with `BaseUrl` configured, the capability is absent and the test skips. See [skip conditions](../foundation/skip-conditions.md).
- `[ReplaceService<T>]` and `[FailDependency<T>]` need the server they substitute on: the named `Server`, or else the test's selected application (then `Default`). With `BaseUrl` configured for that application, the test skips instead of substituting a remote service. Another live server does not keep the gate open.

## Limits

- **A substituting test builds its own server.** It is released with the test, so sharing and parallel safety hold. Under `PerTest`, such a test pays two startups: its own server, then the substituted one.
- **Replacements are singletons of the dedicated server.** A stateful fake stays per test, because the server is per test. A replacement that captures scoped services is the suite's responsibility.
- **A failed dependency throws when resolved.** If the application resolves it during startup, the test's setup fails instead of its requests. Prefer failing services the application resolves per request.
- **Closed-box applications cannot be substituted.** A container or loopback application (`ApplicationContainer`, `AddLoopbackApplication`) exposes no service container to the suite: `[ReplaceService]` and `[FailDependency]` skip, and `Override` throws. The same holds for any future out-of-process host.
- **A loopback provider bridges no clock and no substitution.** The hand-built application has its own container, so the run's `TimeProvider` and service replacements cannot reach it. It declares no `server` and no `clock`, and `[RequiresTestClock]` and `[ReplaceService]` skip for it.
- **The test-user handler replaces the application's default scheme**, and exists only on the test host. A suite whose subject is the application's own authentication leaves it out; the application then ignores the identity header.
- **A startup exception fails the test that starts the server.** A throwing startup filter or middleware factory reaches that test unwrapped. The failed start is rolled back: the test's context is cleared, and no server or client survives. The next test starts the application again, so the fault is retried, not cached.
- **`PerRun` shares application state across tests.** Isolate at the data level, as the sample does with a tenant per test id. `PerTest` pays the startup cost per test.
- **With `BaseUrl` configured, nothing in-process exists.** There is no server, service container, page inventory or `configureWebHost` callback. Tests that need them skip through `[RequiresInProcess]` or a capability condition.
- **One registration per server name on the host overload.** Repeating a name with the same `TProgram` is a no-op, and a different one throws instead of silently serving the first.
- **`ServerFactory` and `ApplicationServices` need a registration under the resolved name.** `ApplicationServices` throws naming the expected call.
- **The page inventory is run-level.** It covers page-like, explicitly-GET endpoints, excludes API-shaped JSON routes, and is not repeated once it has results. A published application never contributes one.

## Links

- [Web overview](./web/index.md) - sessions, options and page coverage.
- [REST clients](./rest/index.md) and [GraphQL clients](./graphql/index.md) - the clients that reuse the in-process transport.
- [Infrastructure](../foundation/infrastructure.md) - how run-scoped settings reach the application.
- [Sample setup](../../../samples/Northstar.ProtoTest/Setup.cs) - the sample suite's real registration.
