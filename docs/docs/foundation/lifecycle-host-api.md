---
sidebar_position: 6
title: Host API
sidebar_label: Host API
description: "The ProtoHost surface for runner integrations: starting and completing tests by hand, and the host members."
---

# Host API

Runner packages call `StartTestAsync` and `CompleteTestAsync` for their tests. The reference below is for runner integrations and extension tests. The order those calls fit into is in [Host and lifecycle](./lifecycle.md).

## Starting and completing a test by hand

```csharp
var host = new ProtoHostBuilder().AddApplication("Api", app => app
    .AddRest(rest => rest.AddClient("Api"))).Build();
await using var ownedHost = host;
await host.StartAsync();

var context = await host.StartTestAsync("my test", testMethod);
// ...
await host.CompleteTestAsync(ProtoTestResult.Passed);
```

`ProtoTestResult` has `Passed`, `Skipped`, `Unknown`, `Failed(exception)`, `Failed(error)` and `Cancelled(exception)`.

The host itself:

```csharp
public sealed class ProtoHost : IAsyncDisposable
{
    static ProtoExecutionContext CurrentContext { get; }
    static ProtoExecutionContext? CurrentContextOrNull { get; }
    static ProtoHost CurrentHost { get; }
    static IProtoTraceWriter? FindTraceWriter(ActivityTraceId traceId);
    static IProtoTraceWriter? FindTraceWriter(Activity? activity);

    IConfiguration Configuration { get; }
    IProtoTraceSource Trace { get; }
    bool HasCapability(string kind, string? name = null);
    bool HasCapability(string kind, string? name, string? instance);
    bool HasApplication(string name);

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);

    Task<ProtoExecutionContext> StartTestAsync(string testName, MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes = null, IProtoTestAttachmentPublisher? attachmentPublisher = null);

    Task CompleteTestAsync();                          // outcome Unknown
    Task CompleteTestAsync(ProtoTestResult result);
}
```

| You need | Overload |
| --- | --- |
| the usual start | `StartTestAsync(testName, testMethod)` |
| an explicit test id | `StartTestAsync(testName, testId, testMethod, ...)` |
| a token for setup I/O | `StartTestAsync(testName, testMethod, cancellationToken)` |
| id and token | `StartTestAsync(testName, testId, testMethod, cancellationToken)` |
| everything at once | `StartTestAsync(testName, testMethod, attributes, attachmentPublisher, cancellationToken)` |

`Proto.Host` returns the host of the current test, or, outside a test, the only active host (with more than one active, it throws).
