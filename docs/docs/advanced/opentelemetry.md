---
sidebar_position: 5
title: OpenTelemetry
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
| an event | an event on the current span |

Spans carry these tags:

| Tag | |
| --- | --- |
| `prototest.test.id` | the test id |
| `prototest.entry.id` | the ProtoTrace entry id |
| `prototest.entry.kind` | e.g. `web.click` |
| `prototest.source` | the integration that wrote it |
| `prototest.phase` | `Setup`, `Execution`, … |
| `prototest.logical_parent_id` | the ProtoTrace parent, when it differs from the span parent |
| `prototest.outcome` | on events |

Large structured values — shape snapshots, serialised context state — stay in the `.prototrace` file only, so spans remain lightweight.

## Correlating with your application

Because operations are real `Activity` instances, `HttpClient`'s standard W3C trace-context propagation applies to requests sent while one is current. If your application is instrumented with OpenTelemetry too, that is what lets its server spans line up under the test step that caused them — this end-to-end path isn't covered by the repository's tests yet, so treat it as expected rather than guaranteed.

:::note
This package has no dedicated test project in the repository yet. The instrumentation it subscribes to is part of `ProtoTest.Core` and is exercised by every run.
:::
