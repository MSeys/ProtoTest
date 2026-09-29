---
sidebar_position: 4
title: OpenTelemetry
description: "Export ProtoTest operations to OpenTelemetry, so test runs land in the same tracing backend as your application."
---

# OpenTelemetry

Every operation ProtoTest starts is also a .NET `Activity` on the `ActivitySource` named **`ProtoTest`**. Subscribe an OpenTelemetry tracer to it with `AddSource("ProtoTest")`, and test runs land in the same backend as your application's telemetry.

## What it is

ProtoTest emits the source. You install the exporter you want and subscribe:

```bash
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;

public sealed class OpenTelemetryHook : IProtoRunHook
{
    private TracerProvider? _provider;

    public Task BeforeRunAsync(CancellationToken cancellationToken = default)
    {
        _provider = Sdk.CreateTracerProviderBuilder()
            .AddSource("ProtoTest")
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

Or set `OTEL_SERVICE_NAME=prototest` in the run environment; OpenTelemetry's default resource reads it.

```csharp
builder.AddRunHook<OpenTelemetryHook>();
```

`AddSource("ProtoTest")` is the whole subscription, because ProtoTest always emits its operations on that source. Exporters, sampling and resource attributes are ordinary OpenTelemetry configuration.

## How to read it

The export is a translation of the ProtoTrace tree:

| ProtoTrace | OpenTelemetry |
| --- | --- |
| an operation | an `Internal` span |
| an event | an event on the span of its parent operation |
| a failed operation | status `Error`, plus an `exception` event with type, message and stack trace |
| a succeeded operation | status `Ok` |

Spans nest the same way the ProtoTrace tree does: a test's `test.setup`, `test.execution` and `test.teardown` spans contain everything that happened in those phases, and an operation started in your test body is a child of `test.execution`. The execution span opens the W3C trace context the test body's calls carry, so application spans those calls cause line up under the test in your backend.

The three phases are three root spans, because they run on separate activities; they share the `prototest.test.id` tag, which is the join key for a test in a backend that shows whole traces. Without a resource of your own, the exporter reports every span under the default service name (`unknown_service:<host>`); set OpenTelemetry's standard `OTEL_SERVICE_NAME` (for example `prototest`) in the run environment, or build a resource in the hook, so test runs are findable next to the application's telemetry.

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
| `prototest.entity.kind`, `prototest.entity.id` | the client, context, server or capability an entry belongs to, when it has one |

The operation's own trace attributes (`http.method`, `web.locator`, your custom ones) are exported as tags too, except values longer than 2,048 characters and the large structured ones (shape snapshots, serialised context state, observation data). Those stay in the `.prototrace` file only, so spans remain lightweight.

### Correlating with your application

Operations are real `Activity` instances, so `HttpClient`'s standard W3C trace-context propagation applies to requests sent while one is current. When your application is instrumented with OpenTelemetry too, that is what lets its server spans line up under the test step that caused them.

## The artifact

The backend trace is a second consumer of the same operations: every operation ProtoTest starts, with its events and outcome, appears as a span in your tracing system. The `.prototrace` archive is unaffected and remains the complete record, because tracing stays on by default and OpenTelemetry is a second consumer of the same operations.

### What does not reach the backend

- **Run-level evidence.** Gate verdicts, target resolutions and skips, capability decisions and run resources are events on the run group in the archive, not spans. The run's identity and environment (`runId`, `environment.*`, the CI metadata you configured) are archive-only too.
- **Application spans captured from your own sources.** A source named in `ProtoTraceOptions.ActivitySources` is recorded into the `.prototrace` tree from the application's own activities, but it is not re-emitted on `ProtoTest`; a backend subscribed only to `ProtoTest` does not show it. Subscribe to the application's source as well to see both in one backend.
- **Values above the tag cap**, listed under Limits below: they exist in the archive, not on the span.

## Limits

- **No exporter included.** Subscribing is one `AddSource` call; install and configure the exporter you want, as above.
- **Large values stay out of spans.** Values longer than 2,048 characters and the structured keys `context.value`, `observation.data`, `observation.metadata`, `shape.expected`, `shape.actual`, `shape.matches` and `shape.mismatches` exist only in `.prototrace`. The same cap applies to spans captured from your application.
- **Propagation reaches an in-process application; an out-of-process one is the environment's.** The HTTP client handler and the raw-request transport inject `traceparent` from the current test's W3C context (pinned by `ClientHandlerTests`), and the configured activity sources capture the application's own spans into the same trace. An application running as its own process joins the trace only when that environment propagates OpenTelemetry context.
- **The end-to-end path is expected, not guaranteed.** The repository's tests pin the injection and the capture; a full test-to-backend round trip is not covered by them yet.

## Learn more

- [ProtoTrace](./prototrace.md): the full record, and the file that carries it.
- [Coverage and observations](./coverage.md): the intentional facts the reports are built from.
- [Run hooks](../foundation/hooks.md): where a `TracerProvider` lives for the run.
