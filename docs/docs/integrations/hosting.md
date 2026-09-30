---
sidebar_position: 9
title: Background workers
sidebar_label: Workers
description: "Run a background worker or generic host in-process with the suite: it starts with the run, reads the run's settings and stops with the run."
---

# Background workers

`ProtoTest.Hosting` runs a background worker inside the test process: it starts with the run, reads the run's settings and stops with the run.

```csharp
[ProtoTest]
public async Task An_invoice_is_created_for_a_metered_session()
{
    var billing = Proto.Context.HostService<BillingWorker.Program, BillingService>("Billing");
    // drive or inspect the running worker
}
```

Run it with `dotnet test`. A green run prints `Passed An_invoice_is_created_for_a_metered_session`, and the trace records a `worker` run entity with the worker's name and program.

## What it adds

It runs a worker application - a generic host with `IHostedService`s, the kind `dotnet new worker` creates - the way [ASP.NET Core](./aspnetcore.md) runs an API. The suite starts the worker's real entry point once per run, after the infrastructure registered before it, and stops it when the run ends.

## Install

```bash
dotnet add package ProtoTest.Hosting
```

## Compose

```csharp
builder
    .AddInfrastructure(
        "AppStore",
        chain => chain
            .UseConfigured()
            .UseContainer(new PostgresDatabase(...)),
        "ConnectionStrings:App")
    .AddInfrastructure(
        "Broker",
        chain => chain
            .UseConfigured()
            .UseContainer(new RabbitMqBroker(...)),
        "ConnectionStrings:Broker")
    .AddWorkerHost<BillingWorker.Program>("Billing");
```

The order is the start order: the worker reads what the infrastructure before it provided, so register containers and other pieces first. `AddWorkerHost` can be called more than once with different names; registering the same name twice is one worker, while the same name with a different program throws instead of silently dropping the second registration.

The suite runs the worker's own program (`Host.CreateApplicationBuilder` or `Host.CreateDefaultBuilder`), with the worker's assembly as its content root and application name - its `appsettings.json`, environment overloads and logging behave as they do in production. The worker's own `Run()` never executes; the suite starts and stops its host.

## The tasks

The `An_invoice_is_created_for_a_metered_session` test above is the whole pattern: resolve the running worker's service, drive or inspect it.

`Proto.Context.Host<TProgram>()` returns the worker's `IHost` for anything the service lookup cannot answer, and both take a name when several workers host the same program.

## Nested under an application

A worker belongs to the application it consumes from: nest it with `AddWorkerHost` on the
application's builder and it follows the application's provider chain instead of a key of its own (see
[Environment resolution](../foundation/environment-resolution.md)):

```csharp
builder.AddApplication("Api", app => app
    .UseConfigured()
    .UseInProcess<Api.Program>()
    .AddWorkerHost<BillingWorker.Program>("Billing", worker => worker
        .UseEnvironment()   // the application runs elsewhere: the environment runs its worker too
        .UseHost()));       // the run hosts the worker's entry point and bridges the test clock
```

- `UseEnvironment()` is available when the application's winner does not run it in-process (a
  configured address, a loopback listener or an AppHost resource). The suite starts nothing: starting
  the worker again would join a staging broker as a second consumer.
- `UseHost()` is the fallback and the only provider that bridges the test clock and lets
  `Proto.Context.Host<TProgram>()` resolve the running host.
- Every winning provider declares the `worker` capability, so `[RequiresWorker<TProgram>]` passes in
  every mode; only a hosted worker declares `clock`, so `[RequiresTestClock]` gates the tests that
  advance the clock against one.
- A worker registered on the host builder (`builder.AddWorkerHost<...>`) is not part of an application;
  its chain defaults to `UseHost`, so the run hosts its entry point and bridges the test clock.

## Configuration

The worker reads, in order of precedence:

1. Its own sources - `appsettings.json`, environment variables, its own command line.
2. The suite's configuration - `builder.ConfigureAppConfiguration(...)`.
3. The run's settings: connection strings from started infrastructure and values from any settings infrastructure.
4. Whatever the suite sets in `AddWorkerHost` options.

The merged overlay reaches the worker's entry point twice: as `--{key}={value}` command-line arguments when the run invokes `Program.Main`, so `Host.CreateApplicationBuilder(args)` and `Host.CreateDefaultBuilder(args)` see final-precedence values in `Main` itself - a connection string read there is the run's - and as an in-memory source applied when the host is built, so an options factory or a hosted service reads the same values. Keys with no value are not passed; `Set(key, null)` is an empty setting, not a dropped key.

The worker builder must be one of the shapes ProtoTest knows: `Host.CreateApplicationBuilder`, `Host.CreateDefaultBuilder`, or `WebApplication.CreateBuilder`. Any other builder fails loudly naming its type, instead of keeping its own configuration and `TimeProvider`.

```csharp
builder.AddWorkerHost<BillingWorker.Program>("Billing", worker =>
    worker.Set("Billing:RetryLimit", "3"));
```

## In the trace and coverage

The worker is run-scoped infrastructure: the trace records a `worker` run entity with its name and program, the run overview lists a `worker` capability, and a release failure follows the same path as any other run resource.

Register the worker after the pieces it needs: a [readiness probe](../foundation/infrastructure-recipes.md#wait-until-it-is-ready) placed before `AddWorkerHost` waits for the broker or database before the worker starts.

## Limits

- **In-process only.** A suite pointed at a published environment has no worker to start. Guard the tests that need one with `[RequiresWorker<TProgram>]` (the typed form of `[RequiresCapability(ProtoCapabilityKinds.Worker)]`, checked by the program assembly name). For an orchestrated topology instead of an in-process worker, see [Aspire](./aspire.md).
- **A nested worker follows its application, not its own key.** `UseEnvironment()` decides from the application's winner; two workers with the same name are one lifecycle host-wide, and a nested worker's name must be unique across applications.
- **A worker is not an application.** It has no address and no clients; if a worker image ever exposes a health endpoint, give it its own application target instead.
- **One instance per run.** The worker is shared by every test in the run, exactly like the in-process application; tests must not assume a fresh worker per test.
- **No per-test lifetime.** `AddWorkerHost` has no `PerTest` option; a worker that must restart between tests is not supported.
- **A parameterless or argument-ignoring `Main` cannot see the overlay before `Build()`.** The run passes the overlay as command-line arguments; an entry point whose `Main()` takes no arguments, or that builds its host without passing `args`, still receives the values when the host is built (the in-memory overlay), so options factories and hosted services read them, but code between creating the builder and calling `Build()` reads only the worker's own sources. A parameterless `Main` also never receives the worker's `--contentRoot`/`--applicationName`, so it reads its own `appsettings.json` from the test process's content root. Build the host from `args` when `Main` itself reads configuration.
- **Start does not wait for readiness.** The host's `StartAsync` completing is the only signal. If a worker must wait for a dependency to be ready, register a [readiness probe](../foundation/infrastructure-recipes.md#wait-until-it-is-ready) before `AddWorkerHost`, or wait inside its own service.
- **The worker host runs in the test process.** Its background threads, loggers and static state are the test process's; a worker that must be killed hard is not a good fit.

## Learn more

- [Infrastructure](../foundation/infrastructure.md) - containers and settings the worker reads.
- [REST](./rest/index.md), [Messaging](./messaging/index.md) - talking to what the worker consumes and produces.
