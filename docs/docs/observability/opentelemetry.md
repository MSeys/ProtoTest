---
sidebar_position: 4
title: OpenTelemetry
description: "Export ProtoTest operations to OpenTelemetry, so test runs land in the same tracing backend as your application."
---

# OpenTelemetry

Every ProtoTrace operation is also a .NET `Activity` on the `ActivitySource` named **`ProtoTest`**. `ProtoTest.OpenTelemetry` is a one-line bridge that subscribes an OpenTelemetry tracer to it, so test runs can land in the same backend as your application's telemetry.

```bash
dotnet add package ProtoTest.OpenTelemetry
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using ProtoTest.OpenTelemetry;

public sealed class OpenTelemetryHook : IProtoRunHook
{
    private TracerProvider? _provider;

    public Task BeforeRunAsync(CancellationToken cancellationToken = default)
    {
        _provider = Sdk.CreateTracerProviderBuilder()
            .AddProtoTestInstrumentation()
            .AddOtlpExporter()
            .Build();
        return Task.CompletedTask;
    }

    public Task AfterRunAsync(CancellationToken cancellationToken = default)
    {
        _provider?.Dispose();   // flushes pending spans
        return Task.CompletedTask;
    }
}
```

```csharp
builder.AddRunHook<OpenTelemetryHook>();
```

The package adds exactly one method — `AddProtoTestInstrumentation()`, which is `AddSource("ProtoTest")`. Exporters, sampling and resource attributes are ordinary OpenTelemetry configuration.

## What's exported

| ProtoTrace | OpenTelemetry |
| --- | --- |
| an operation | an `Internal` span |
| an event | an event on the span of its parent operation |
| a failed operation | status `Error`, plus an `exception` event with type, message and stack trace |
| a succeeded operation | status `Ok` |

Spans nest the same way the ProtoTrace tree does: a test's `test.setup`, `test.execution` and `test.teardown` spans contain everything that happened in those phases, and an operation started in your test body is a child of `test.execution` — so one test is one connected trace in your backend.

Spans and events carry these tags:

| Tag | |
| --- | --- |
| `prototest.test.id` | the test id |
| `prototest.entry.id` | the ProtoTrace entry id |
| `prototest.entry.kind` | e.g. `web.click` |
| `prototest.source` | the integration that wrote it |
| `prototest.phase` | `setup`, `execution`, `teardown`, `rollback` or `run` |
| `prototest.outcome` | `succeeded`, `failed`, `partial`, `cancelled`, `skipped` or `unknown` |
| `prototest.logical_parent_id` | the ProtoTrace parent entry id (spans only) |

The operation's own trace attributes — `http.method`, `web.locator`, your custom ones — are exported as tags too, except values longer than 2,048 characters and the large structured ones (shape snapshots, serialised context state, observation data). Those stay in the `.prototrace` file only, so spans remain lightweight.

## Correlating with your application

Because operations are real `Activity` instances, `HttpClient`'s standard W3C trace-context propagation applies to requests sent while one is current. If your application is instrumented with OpenTelemetry too, that is what lets its server spans line up under the test step that caused them — this end-to-end path isn't covered by the repository's tests yet, so treat it as expected rather than guaranteed.