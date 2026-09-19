---
sidebar_position: 1
title: Extending ProtoTest
description: "The public extension points behind every built-in integration, for writing your own capability that feels native to ProtoTest."
---

# Extending ProtoTest

Everything the built-in integrations do is built on public extension points. This page is the map, for when you want your own capability — a message-bus client, a file-assertion helper, a company-specific environment — to feel native.

| You want to… | Use |
| --- | --- |
| package setup for *some* tests | a [`ProtoAttribute`](../foundation/attributes.md) |
| run code around *every* test or the whole run | a [hook](../foundation/hooks.md) |
| give tests a new client | a [client initializer](../foundation/clients.md) + an extension method |
| pass data between setup and tests | [typed state](../foundation/execution-context.md#typed-state) |
| authenticate HTTP requests | an [`IProtoHttpAuthenticator`](../integrations/rest/authentication.md#writing-your-own) |
| log a browser in | an [`IWebLoginStrategy`](../integrations/web/login.md) |
| wait for app-specific readiness | an [`IWebWaitCondition`](../integrations/web/middleware.md) |
| create data in your system | an [`IProtoDataProvisioner`](../integrations/data/provisioners.md) |
| report on what tests did | observations + a [collector](../observability/coverage.md#writing-a-collector) |
| write reports somewhere | an [`IProtoSink`](../observability/reporting.md#writing-a-sink) |
| show up in the trace viewer | the trace writer — below |
| support another test runner | `ProtoHost` + `IProtoTestAttachmentPublisher` — below |

## Building an integration

A typical integration package has four parts. Here's a sketch for a hypothetical message-bus client:

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

    public async Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var connection = await Connection.OpenAsync(options.Endpoint, cancellationToken);
        context.RegisterClient(new BusClient(connection, context), name);
        return true;
    }
}
```

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

### Configuration

To make options bindable from `appsettings.json` like the built-in ones, implement `IProtoConfigurableOptions` and call `BindFromConfiguration` after applying the code callback:

```csharp
public sealed class BusOptions : IProtoConfigurableOptions
{
    public string ConfigurationSectionName => "ProtoTest:Bus";
    public string Endpoint { get; set; } = "amqp://localhost";
}

options.BindFromConfiguration(configuration);   // configuration wins over code
```

### Authenticator-style construction

`ProtoAuthenticatorFactory.Create<T>(context, constructorArgs)` is what `[Auth<T>]` and `[LoginAs<T>]` use to build their types: positional arguments from an attribute, the rest from DI and the `ProtoExecutionContext`. Use it for your own generic attributes so they behave the same way.

## Adding to the trace

`context.Trace` is an `IProtoTraceWriter`.

### Operations

The easiest way is `ExecuteAsync`, which records success, failure or cancellation for you and rethrows. The action is a `Func<ValueTask>` (or `Func<ValueTask<T>>`), so an `async` lambda works for any awaitable:

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
    "ProtoTest.SampleApp.Testing",
    outcome: ProtoTraceOutcome.Succeeded,
    attributes: new Dictionary<string, string?> { ["saas.correlation_id"] = correlationId });
```

### Conventions

- **Kind** is a dotted, lowercase identifier: `{area}.{action}`. The viewer groups by the prefix, so new kinds get their own category without a viewer release.
- **Name** is for humans: the built-ins use `AREA · Verb · subject`.
- **Source** is your package name.
- **Attributes** are strings. Keep them small and never put secrets in them.

Nesting is automatic: an operation started while another is running becomes its child, and the phase is inherited. Pass `parentId` to attach elsewhere, or `phase` to set it explicitly.

## Supporting another runner

A runner integration needs three things, all visible in the existing runner packages:

1. Build and start one `ProtoHost` per process, and stop it at the end.
2. Around each test, call `ProtoAttributeResolver.Resolve(method)` and then `StartTestAsync(name, method, attributes, publisher)`; afterwards, `CompleteTestAsync(result)` with the best outcome the runner can tell you.
3. Implement `IProtoTestAttachmentPublisher` using the runner's own attachment API.

Start the test on the same async flow the test body will run on — `Proto.Context` depends on it.
