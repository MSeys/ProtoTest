---
sidebar_position: 6
title: ASP.NET Core
---

# ASP.NET Core

`ProtoTest.AspNetCore` runs your ASP.NET Core application in-process with `WebApplicationFactory`, and hands its `HttpClient` to the REST and GraphQL clients. No deployed environment, no ports.

```bash
dotnet add package ProtoTest.AspNetCore
```

## Registering

```csharp
builder
    .AddRest(rest => rest.AddClient("Api"))
    .AddAspNetCoreServer<Program>("Api");
```

```csharp
public static IProtoHostBuilder AddAspNetCoreServer<TProgram>(
    this IProtoHostBuilder builder,
    string name = "Default",
    Action<IWebHostBuilder>? configureWebHost = null,
    Action<WebApplicationFactoryClientOptions>? configureClient = null)
    where TProgram : class;
```

The **name is the client name**: `Proto.Context.Rest("Api")` now talks to the in-process application. `TProgram` is your application's entry point — for minimal APIs, add `public partial class Program;` to the application so the test project can see it.

For each test, ProtoTest creates the factory, registers it on the context as `"{name}:Factory"`, and registers `factory.CreateClient(...)` as the client `name`.

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
    configureClient: client => client.AllowAutoRedirect = false);
```

## Reaching into the application

```csharp
WebApplicationFactory<TProgram> Server<TProgram>(this ProtoExecutionContext context, string name = "Default");
IServiceScope CreateServerScope<TProgram>(this ProtoExecutionContext context, string name = "Default");
TService ServerService<TProgram, TService>(this ProtoExecutionContext context, string name = "Default");
```

```csharp
var emails = Proto.Context.ServerService<Program, IEmailSender>("Api");

using var scope = Proto.Context.CreateServerScope<Program>("Api");
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
```

`Server<TProgram>()` also gives you the underlying `TestServer` — the GraphQL [WebSocket factory](./graphql/subscriptions.md#supplying-your-own-websocket) uses it to open in-process WebSockets.

## Real server or in-process?

A client name can be served by a configured URL *or* by the in-process server, and ProtoTest picks one per test. Initializers are tried in **registration order**, and the first one able to create the client wins:

| Registration order | `ProtoTest:Clients:Api:BaseUrl` set? | Result |
| --- | --- | --- |
| `AddRest(… "Api")` then `AddAspNetCoreServer("Api")` | yes | real server at the configured URL |
| `AddRest(… "Api")` then `AddAspNetCoreServer("Api")` | no | in-process server |
| `AddAspNetCoreServer("Api")` then `AddRest(… "Api")` | either | in-process server |

The first row is the useful one: the same suite runs in-process on a developer machine and against a deployed environment in CI, just by setting `BaseUrl` there.

Each start is recorded as an `aspnetcore.server.initialize` trace event.
