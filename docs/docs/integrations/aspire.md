---
sidebar_position: 8
title: Aspire
description: "Run an Aspire AppHost with the suite: the run starts it, each resource becomes an application target, and the run releases it."
---

# Aspire

`ProtoTest.Aspire` runs an Aspire AppHost with the suite: the run starts the AppHost's own entry point after the infrastructure registered before it, each declared resource's endpoint is published as its application's `BaseUrl`, and the run stops the AppHost after the reports are written. Per-test contexts and the trace work on top, unchanged — a test resolves the address and calls it.

```bash
dotnet add package ProtoTest.Aspire
```

The package targets `net8.0`, `net9.0` and `net10.0`, and the Aspire release is pinned once centrally: `Aspire.Hosting.Testing` in `Directory.Packages.props`, the `Aspire.AppHost.Sdk` version in `global.json`. The test AppHost itself is an Aspire AppHost SDK project, so restoring the suite needs NuGet access.

## Registering

```csharp
builder
    .AddAspireAppHost<TestAppHostAnchor>("api")
    .AddHttpReadiness("api", "/")
    .AddApplication("api", app => app
        .AddRest(rest => rest.AddClient("api")));
```

The order is the start order: register containers and other pieces first when the AppHost needs them, and register the readiness probe after the AppHost — probes are awaited at their registration position, so a probe registered first resolves nothing. The AppHost **serves only when selected**: `ProtoTest:Aspire:Enabled` (or a resource's own `ProtoTest:Aspire:Resources:{resource}:Enabled`) starts it, and without either key it never starts and the suite resolves its targets elsewhere. A run that configures every declared resource's `ProtoTest:Applications:{resource}:BaseUrl` satisfies all the keys the AppHost would fill, so the AppHost never starts even when selected and the same suite runs against that environment. When only some are configured, the AppHost still starts for the rest and publishes only the selected keys configuration does not already fill, so a configured address is never masked by the started AppHost.

`TEntryPoint` is any public type in the AppHost assembly — the testing host runs the assembly's entry point in-process. A top-level `Program` is internal, so expose an anchor:

```csharp
namespace MyApp.AppHost;

/// <summary>Anchor for the suite: pins the AppHost assembly for the Aspire testing host.</summary>
public sealed class AppHostAnchor;
```

`AddAspireAppHost` can be called more than once for different AppHosts; repeating the same entry point with the same composition is one AppHost, while the same entry point with different resources throws instead of silently dropping the second registration. Registering one resource under two AppHosts throws the same way.

## Serving targets through the chain

An AppHost can be one provider among others: register it, then reference its resources from the
targets they serve. The AppHost serves a target when the integration-owned selection key
`ProtoTest:Aspire:Enabled` is set (an environment variable in the run script), or when the served
resource's own key `ProtoTest:Aspire:Resources:{resource}:Enabled` is set: the global key selects
every AppHost provider, the resource's key only that resource's. A configured provider earlier in the
chain wins over it, and a target it serves needs no plain registration.

```csharp
builder
    .AddAspireAppHost<OpenCsmsAppHost>(options => options
        .MapConnectionString("postgres", "ConnectionStrings:Csms"),
        "api")
    .AddApplication("Csms", app => app
        .UseConfigured()
        .UseAspireResource<OpenCsmsAppHost>("api")
        .UseInProcess<CsmsApi>())
    .AddInfrastructure("CsmsDatabase", piece => piece
        .UseConfigured()
        .UseAspireResource<OpenCsmsAppHost>("postgres")
        .UseContainer(PostgresDatabase.Container()),
        "ConnectionStrings:Csms");
```

- `UseAspireResource<OpenCsmsAppHost>("api")` on an application chain publishes the resource's
  endpoint under the application's derived `BaseUrl`. On an infrastructure chain it publishes the
  resource's **connection string** under every key the target declares.
- `ProtoAspireOptions.MapConnectionString` (or `builder.MapConnectionString(resource, key)` after
  registering the AppHost) fills a key no target declares.
- The AppHost starts **once** when it wins any target, at its registration position; a losing provider
  never starts it, and a target whose configured provider wins skips it without starting anything.
- The mixed compositions are one selection key away. Setting only
  `ProtoTest__Aspire__Resources__postgres__Enabled=true` (and the broker's own key) runs the store and
  broker through the AppHost while the application stays in-process; setting only
  `ProtoTest__Aspire__Resources__api__Enabled=true` runs the AppHost's application against suite
  containers. `ProtoTest:Aspire:Enabled` keeps meaning "every AppHost provider".

The run hands the AppHost its configuration, the settings earlier infrastructure published and the
options' `Set` values, as command-line arguments in that order (each layer wins over the one before
it). The AppHost's own graph reads them - `builder.Configuration["ConnectionStrings:Csms"]` decides
whether it declares its own store, and `AddConnectionString`/`WithEnvironment` bridges a provided
value into its projects - so a suite container's address reaches the AppHost exactly as it reaches
the suite's readers.

The plain `AddAspireAppHost` registration follows the same selection semantics: it is one target whose
providers are the configured step-aside, the selected AppHost provider and a fallback, so a suite that
registers it without a selection key keeps resolving its targets through the other providers, and the
AppHost starts only when a selection key is set and at least one key it fills is not configured. The
skip record names the condition (`unselected`, or the missing keys for `configured`) like any other
chain.

## Reaching it from a test

```csharp
[ProtoTest]
public async Task Health_endpoint_answers()
{
    var address = Proto.Context.AspireResource("api");

    using var http = new HttpClient();
    using var response = await http.GetAsync(address);
    response.EnsureSuccessStatusCode();
}
```

`AspireResource` returns the base address the AppHost published for the resource — or the configured address when the run points at a deployed topology. The application's REST, GraphQL and gRPC clients resolve the same address, so the journey in [Registering](#registering) calls the resource through the normal client. The suite's composition for one AppHost, one probe and one client is in `tests/ProtoTest.Aspire.Tests/AspireAppHostTests.cs`.

## Options and keys

```csharp
builder.AddAspireAppHost<TestAppHostAnchor>(
    options => options
        .MapResource("api", "Api")
        .UseEndpoint("api", "https")
        .Set("Seed:Catalog", "smoke"),
    "api");
```

| Key | Type | Default | |
| --- | --- | --- | --- |
| `ProtoTest:Aspire:Enabled` | bool | unset → no AppHost provider serves | the global selection key; set it (`ProtoTest__Aspire__Enabled=true`) to resolve every AppHost provider's targets through the AppHost |
| `ProtoTest:Aspire:Resources:{resource}:Enabled` | bool | unset → only the global key selects the resource | the per-resource selection key; set it (`ProtoTest__Aspire__Resources__api__Enabled=true`) to resolve that resource's targets through the AppHost while the run's other targets stay on their other providers |
| `ProtoTest:Applications:{resource}:BaseUrl` | string | unset → the AppHost publishes it | set it to point the suite at a deployed topology instead of starting the AppHost; a configured key wins over the started AppHost's address for that resource |

`MapResource` publishes the resource under a different application name; `UseEndpoint` reads a non-default endpoint of the resource (the default is `http`); `MapConnectionString` publishes a resource's connection string under a target's key instead of an address; `Set` passes a setting to the AppHost as a command-line argument on top of the run's configuration and the settings earlier infrastructure published — the AppHost otherwise reads its own sources. Two resources cannot share one application name.

## Context API

```csharp
string AspireResource(this ProtoExecutionContext context, string resource);
```

An unknown resource throws naming `AddAspireAppHost` and the resources the run composed; a resource whose address is neither published nor configured throws naming its `BaseUrl` key.

## What it records

The AppHost is run-scoped infrastructure: the trace records an `aspire` run entity named `Aspire AppHost · {assembly}`, the run overview lists an `aspire` capability named for the AppHost assembly, and the entity carries the published endpoints as evidence (`aspire.resource.{resource}.address`, with the endpoint name beside it; a resource whose key configuration already fills carries `aspire.resource.{resource}.address_source = configuration`, and one another provider serves carries `..._source = not selected`). A release failure follows the same path as any other run resource.

## Choosing between a worker and an AppHost

`ProtoTest.Hosting` runs a worker's own entry point **in-process**: the test and the worker share a process, a container and a clock, and white-box accessors reach the worker's services. `ProtoTest.Aspire` runs an AppHost as a **closed box**: the topology starts outside the test process and the suite only sees its endpoints. Choose the worker when the test drives or inspects the host's services; choose the AppHost when the test must prove the deployed topology — service discovery, ports and process boundaries included. Neither substitutes for the other: the closed-box limits below apply only here.

## Skip

- The registration adds the capability `aspire` named for the AppHost assembly while the AppHost serves - selected and not fully configured - so `[RequiresCapability("aspire")]` proves composition. Tests that need one resource's application target use `[RequiresApplication]`.
- The AppHost needs Aspire's orchestration binaries at runtime. A machine without them fails the start with `ProtoAspireUnavailableException`; catch it to skip with its reason instead of failing:

```csharp
try
{
    await host.StartAsync();
}
catch (ProtoAspireUnavailableException exception)
{
    Assert.Ignore($"The Aspire orchestration runtime is unavailable: {exception.Message}");
    return;
}
```

## Limits

- **Closed box.** No per-test service substitution and no in-process assertions: the suite cannot reach the AppHost's container, replace its services or read its memory. White-box tests belong in `ProtoTest.Hosting`.
- **The suite's AppHost leg is `net10.0`.** The package ships `net8.0`/`net9.0`/`net10.0` assets; the test AppHost cannot multi-target because DCP launches its project resources with `dotnet run`, which cannot choose a target framework for a multi-targeted project.
- **One instance per run.** The AppHost is shared by every test in the run; tests must not assume a fresh topology per test.
- **No per-test lifetime.** `AddAspireAppHost` has no `PerTest` option; a topology that must restart between tests is not supported.
- **Start does not wait for readiness.** `StartAsync` completing means the AppHost accepted the topology, not that every resource answers. Register `AddHttpReadiness` after the AppHost for the HTTP resources tests call.
- **A configured address wins per resource.** Every declared key configured skips the AppHost entirely even when selected; with only some configured it starts and publishes only the selected missing keys, so `AspireResource` returns the configured value for one resource and the AppHost's address for another.
- **An AppHost serves only when selected.** Every AppHost provider - the ones `UseAspireResource` adds and the one `AddAspireAppHost` registers - holds on the global `ProtoTest:Aspire:Enabled` or on the resource's own `ProtoTest:Aspire:Resources:{resource}:Enabled`; a suite that registers the AppHost but sets neither key never starts it and resolves every target through its other providers. The AppHost publishes only the selected resources' keys, so selecting one resource leaves the others on their other providers.
- **The AppHost reads the run's settings, it does not inherit the run's graph.** The configuration, the settings earlier infrastructure published and the options travel to the AppHost as command-line arguments; bridging them into the AppHost's projects (`AddConnectionString`, `WithEnvironment`, a conditional resource) stays the AppHost project's code, because only it knows its graph.
- **A connection-string resource has no endpoint.** `MapConnectionString` replaces the resource's endpoint publish, and a resource that exposes neither an endpoint nor a connection string fails the AppHost start naming the resource.
- **The AppHost project restores the Aspire SDK from NuGet.** Offline restores cannot build a suite that composes one.

## Links

- [Background workers](./hosting.md) — the in-process alternative and how to choose.
- [Infrastructure](../foundation/infrastructure.md) — run-scoped settings and readiness.
- [REST clients](./rest/index.md) — calling a published resource through its application.
