---
sidebar_position: 1
title: Extending ProtoTest
description: "The public extension points behind every built-in integration, and the contract a package outside this repository follows."
---

# Extending ProtoTest

Every built-in integration is built on public types, and a package outside this repository uses the same ones. This page is the contract: which extension point to use, what it guarantees, and how the result reads in the trace.

## The contract

- **The extension points are public.** `tests/ProtoTest.Extensibility.Tests` compiles a minimal broker adapter and a minimal web backend against the public surface only, so a point that regresses to internals fails that build. An integration never reaches into another package's internals.
- **One mechanism per concern.** A client is a client initializer, a wait is a readiness probe, run state is infrastructure, a capability is a capability descriptor, and evidence goes through the trace writer. An extension uses those mechanisms; it does not add a second lifecycle.
- **Options follow one shape.** An options type implements `IProtoConfigurableOptions`, registers through `ProtoOptionsRegistration`, and binds its section over the code callback, so configuration wins the same way everywhere.
- **The run composes your package like any other.** A builder extension registers services on the `IProtoHostBuilder`; the host owns start, stop and release from there.

## Where each extension point lives

| You want to… | Use |
| --- | --- |
| package setup for *some* tests | a [`ProtoAttribute`](../foundation/attributes.md) |
| run code around *every* test or the whole run | a [hook](../foundation/hooks.md) |
| give tests a new client | a [client initializer](../foundation/clients.md) plus an extension method |
| publish and await over your own broker | an [`IProtoMessageBroker`](../integrations/messaging/index.md#the-adapter-contract) whose consumer derives from [`ProtoMessageConsumerBase`](../integrations/messaging/index.md#writing-an-adapter) |
| pass data between setup and tests | [typed state](../foundation/execution-context.md#typed-state) |
| authenticate HTTP requests | an [`IProtoHttpAuthenticator`](../integrations/rest/authentication.md#writing-your-own) |
| log a browser in | an [`IWebLoginStrategy`](../integrations/web/login.md) |
| wait for app-specific readiness | an [`IWebWaitCondition`](../integrations/web/middleware.md) |
| create data in your system | an [`IProtoDataProvisioner`](../integrations/data/provisioners.md) |
| report on what tests did | observations plus a [collector](../observability/coverage.md#writing-a-collector) |
| write reports somewhere | an [`IProtoSink`](../observability/reporting.md#writing-a-sink) |
| show up in the trace viewer | the trace writer, below |
| support another test runner | `ProtoHost` plus `IProtoTestAttachmentPublisher`, below |

## A worked example: a message-bus client

The sketch below is a hypothetical message-bus client. It has the four parts a typical integration has.

**1. The client and its initializer**

```csharp
public sealed class BusClient(IConnection connection, ProtoExecutionContext context) : IAsyncDisposable
{
    public async Task PublishAsync(string topic, object message)
    {
        await context.Trace.ExecuteAsync(
            "bus.publish",
            $"BUS · Publish · {topic}",
            "Acme.ProtoTest.Bus",
            async () => await connection.PublishAsync(topic, message),
            attributes: new Dictionary<string, string?> { ["bus.topic"] = topic });

        context.RecordObservation("Bus", "bus.publish", topic);
    }

    public ValueTask DisposeAsync() => connection.DisposeAsync();
}

internal sealed class BusClientInitializer(string name, BusOptions options) : IProtoClientInitializer<BusClient>
{
    public string Name => name;

    public async Task<bool> TryInitializeAsync(ProtoExecutionContext context)
    {
        var connection = await Connection.OpenAsync(options.Endpoint, context.CancellationToken);
        context.RegisterClient(new BusClient(connection, context), name);
        return true;
    }
}
```

The initializer reads the test's cancellation token and passes it to its own I/O. A client that a test shares across tests must not hold the context: it resolves the context per call, only while it acts on the test's flow. See [Context lookups](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md#context-lookups).

**2. A host builder extension**

```csharp
public static class ProtoHostBuilderBusExtensions
{
    public static IProtoHostBuilder AddBus(this IProtoHostBuilder builder, string name, Action<BusOptions> configure)
    {
        var options = new BusOptions();
        configure(options);
        return builder.ConfigureServices(services =>
            services.AddSingleton<IProtoClientInitializer>(new BusClientInitializer(name, options)));
    }
}
```

**3. A context extension**

```csharp
public static class ProtoExecutionContextBusExtensions
{
    public static BusClient Bus(this ProtoExecutionContext context, string name = "Default") =>
        context.Client<BusClient>(name);
}
```

**4. Optionally, a collector** consuming your `bus.publish` observations, so topics show up in coverage reports.

The result reads like everything else:

```csharp
builder.AddBus("Default", bus => bus.Endpoint = "amqp://localhost");

await Proto.Context.Bus().PublishAsync("orders.created", order);
```

### Options and configuration

To make options bindable from `appsettings.json` like the built-in ones, implement `IProtoConfigurableOptions` and call `BindFromConfiguration` after applying the code callback:

```csharp
public sealed class BusOptions : IProtoConfigurableOptions
{
    public string ConfigurationSectionName => "Acme:Bus";
    public string Endpoint { get; set; } = "amqp://localhost";
}

options.BindFromConfiguration(configuration);   // configuration wins over code
```

The section name follows `ProtoTest:<Integration>[:<Area>]`, where the area names the options type's role. A renamed section returns its old name from `FallbackConfigurationSectionName`, so the old key keeps working as a documented fallback.

### Authenticator-style construction

`ProtoAuthenticatorFactory.Create<T>(context, constructorArgs)` is what `[Auth<T>]` and `[LoginAs<T>]` use to build their types: positional arguments from an attribute, the rest from DI and the `ProtoExecutionContext`. Use it for your own generic attributes so they behave the same way.

## Adding to the trace

`context.Trace` is an `IProtoTraceWriter`.

### Operations

The easiest way is `ExecuteAsync`, which records success, failure or cancellation for you and rethrows. The action is a `Func<ValueTask>` or `Func<ValueTask<T>>`, so an `async` lambda works for any awaitable:

```csharp
await context.Trace.ExecuteAsync(
    kind: "files.assert",
    name: $"FILE · Assert · {fileName}",
    source: "Acme.ProtoTest.Files",
    action: async () => await AssertWorkbookAsync(fileName),
    attributes: new Dictionary<string, string?> { ["file.name"] = fileName });

var rows = await context.Trace.ExecuteAsync(
    "files.read", $"FILE · Read · {fileName}", "Acme.ProtoTest.Files",
    async () => await ReadRowsAsync(fileName));
```

Or manage an operation yourself:

```csharp
using var operation = context.Trace.StartOperation(
    "files.assert", $"FILE · Assert · {fileName}", "Acme.ProtoTest.Files");

try
{
    var mismatches = await CompareAsync(fileName);
    operation.SetAttribute("file.mismatches", mismatches.Count.ToString());

    if (mismatches.Count == 0) operation.Succeed();
    else operation.Complete(ProtoTraceOutcome.Partial);
}
catch (Exception exception)
{
    operation.Fail(exception);
    throw;
}
```

`ProtoTraceOperation` has `Id`, `SetAttribute(name, value)` (chainable), `Succeed()`, `Fail(exception)`, `Cancel(exception?)` and `Complete(outcome, exception?)`. It completes only once; disposing it without completing records `Unknown`.

### Events

```csharp
context.Trace.WriteEvent(
    "saas.correlation.begin",
    "Begin correlated SaaS scenario",
    "Northstar.ProtoTest",
    outcome: ProtoTraceOutcome.Succeeded,
    attributes: new Dictionary<string, string?> { ["saas.correlation_id"] = correlationId });
```

### Conventions

- **Kind** is a dotted, lowercase identifier: `{area}.{action}`. The viewer groups by the prefix, so a new kind gets its own category without a viewer release.
- **Name** is for humans: the built-ins use `AREA · Verb · subject`.
- **Source** is your package name.
- **Attributes** are strings. Keep them small, and never put secrets in them.

Nesting is automatic: an operation started while another is running becomes its child, and the phase is inherited. Pass `parentId` to attach elsewhere, or `phase` to set it explicitly.

## Supporting another runner

A runner integration needs three things, all visible in the existing runner packages:

1. Build and start one `ProtoHost` per process, and stop it at the end.
2. Around each test, call `ProtoTestAdapter.Prepare(method, host)` and start the returned preparation with `StartAsync(host, publisher)`. Skip through your runner's own mechanism when `CanRun` is false; afterwards, call `CompleteTestAsync(result)` with the best outcome the runner can tell you. `ProtoTestResult` has factories for passed, skipped, partial, failed and cancelled.
3. Implement `IProtoTestAttachmentPublisher` using the runner's own attachment API.

Start the test on the same async flow the test body will run on, because `Proto.Context` depends on it. The shared compliance suite in `tests/ProtoTest.AdapterContract` is what every adapter package runs; extend it instead of forking it.

## Limits of the contract

- **There is no internal surface.** An extension compiles against the public packages only; when it needs a type it cannot see, the owning package publishes the contract or moves the code. If you believe a point is missing, open an issue so it can be added deliberately. See [Community packages](https://github.com/MSeys/ProtoTest/blob/main/CONTRIBUTING.md#community-packages).
- **A capability is declared only by something that can serve it.** An extension declares its capability while its address can be provided; where the address is missing, the capability is absent and gated tests skip instead of failing at first use.
- **The trace viewer contract is not edited casually.** A new kind or attribute is additive; a viewer release is not needed for a new prefix.
- **One mechanism per concern is a review rule.** A second host builder, resource registry, capability model or evidence boundary is not an extension; it is the failure mode this contract exists to prevent.
