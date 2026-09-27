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

The order is the start order: register containers and other pieces first when the AppHost needs them, and register the readiness probe after the AppHost — probes are awaited at their registration position, so a probe registered first resolves nothing. A run that configures every declared resource's `ProtoTest:Applications:{resource}:BaseUrl` satisfies all the keys the AppHost would fill, so the AppHost never starts and the same suite runs against that environment. When only some are configured, the AppHost still starts for the rest and publishes only the keys configuration does not already fill, so a configured address is never masked by the started AppHost.

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
`ProtoTest:Aspire:Enabled` is set (an environment variable in the run script); a configured provider
earlier in the chain wins over it, and a target it serves needs no plain registration.

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
- `ProtoTest:Aspire:Enabled` is the only selection key: `run-suite.ps1 -Mode topology` sets it in the
  reference suite, and nothing else reads it.

This is the behavior change the package adopts for chains: the old "starts unless every resource key is
configured" remains the behavior of the plain `AddAspireAppHost` registration (kept for suites that
register no chain), while a chain's AppHost serves when selected and a configured provider earlier in
the chain wins.

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
| `ProtoTest:Aspire:Enabled` | bool | unset → a chain's AppHost providers do not serve | the integration-owned selection key; set it (`ProtoTest__Aspire__Enabled=true`) to resolve the suite's targets through the AppHost |
| `ProtoTest:Applications:{resource}:BaseUrl` | string | unset → the AppHost publishes it | set it to point the suite at a deployed topology instead of starting the AppHost; a configured key wins over the started AppHost's address for that resource |

`MapResource` publishes the resource under a different application name; `UseEndpoint` reads a non-default endpoint of the resource (the default is `http`); `MapConnectionString` publishes a resource's connection string under a target's key instead of an address; `Set` passes a setting to the AppHost as a command-line argument — the AppHost otherwise reads its own sources. Two resources cannot share one application name.

## Context API

```csharp
string AspireResource(this ProtoExecutionContext context, string resource);
```

An unknown resource throws naming `AddAspireAppHost` and the resources the run composed; a resource whose address is neither published nor configured throws naming its `BaseUrl` key.

## What it records

The AppHost is run-scoped infrastructure: the trace records an `aspire` run entity named `Aspire AppHost · {assembly}`, the run overview lists an `aspire` capability named for the AppHost assembly, and the entity carries the published endpoints as evidence (`aspire.resource.{resource}.address`, with the endpoint name beside it; a resource whose key configuration already fills carries `aspire.resource.{resource}.address_source = configuration` instead). A release failure follows the same path as any other run resource.

## Choosing between a worker and an AppHost

`ProtoTest.Hosting` runs a worker's own entry point **in-process**: the test and the worker share a process, a container and a clock, and white-box accessors reach the worker's services. `ProtoTest.Aspire` runs an AppHost as a **closed box**: the topology starts outside the test process and the suite only sees its endpoints. Choose the worker when the test drives or inspects the host's services; choose the AppHost when the test must prove the deployed topology — service discovery, ports and process boundaries included. Neither substitutes for the other: the closed-box limits below apply only here.

## Skip

- The registration adds the capability `aspire` named for the AppHost assembly, so `[RequiresCapability("aspire")]` proves composition. Tests that need one resource's application target use `[RequiresApplication]`.
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
- **A configured address wins per resource.** Every declared key configured skips the AppHost entirely; with only some configured it starts and publishes only the missing keys, so `AspireResource` returns the configured value for one resource and the AppHost's address for another.
- **A chain's AppHost serves only when selected.** `UseAspireResource` providers hold on `ProtoTest:Aspire:Enabled`; a suite that adds the providers but never sets the key resolves every target through its other providers and never starts the AppHost. The plain `AddAspireAppHost` registration keeps the older all-configured rule.
- **A connection-string resource has no endpoint.** `MapConnectionString` replaces the resource's endpoint publish, and a resource that exposes neither an endpoint nor a connection string fails the AppHost start naming the resource.
- **The AppHost project restores the Aspire SDK from NuGet.** Offline restores cannot build a suite that composes one.

## Links

- [Background workers](./hosting.md) — the in-process alternative and how to choose.
- [Infrastructure](../foundation/infrastructure.md) — run-scoped settings and readiness.
- [REST clients](./rest/index.md) — calling a published resource through its application.
