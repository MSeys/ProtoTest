# ProtoTest.OpenTelemetry

Registers ProtoTest's built-in `ActivitySource` with an OpenTelemetry tracing pipeline; the local `.prototrace` archive stays independent.

```bash
dotnet add package ProtoTest.OpenTelemetry
```

Install an exporter such as `OpenTelemetry.Exporter.OpenTelemetryProtocol` alongside this package when needed.

## Quick start

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using ProtoTest.OpenTelemetry;

using var provider = Sdk.CreateTracerProviderBuilder()
    .AddProtoTestInstrumentation()
    .AddOtlpExporter()
    .Build();
```

The provider can live in a ProtoTest run hook so it covers the whole run.

## What it adds

- **Instrumentation** — the single extension `AddProtoTestInstrumentation()` adds the `"ProtoTest"` activity source to the caller-owned `TracerProviderBuilder`.
- **Operations as spans** — each ProtoTest operation becomes an `ActivityKind.Internal` span, with tags such as `prototest.test.id`, `prototest.entry.kind`, `prototest.source`, `prototest.phase`, `prototest.entity.kind`/`id` and `prototest.outcome`.
- **Events as span events** — instantaneous trace entries attach as `ActivityEvent`s to the span of their parent operation when one exists.
- **Outcome mapping** — a failed operation sets `Error` with a description plus an `exception` event (type, message, stack trace); a succeeded operation sets `Ok`.
- **Pipeline-agnostic** — the caller owns the `TracerProviderBuilder`; exporters, sampling and resources are ordinary OpenTelemetry configuration, and the package includes no exporter.
- **Stable source name** — the added source is always `"ProtoTest"`, so the bridge needs no per-run setup.

ProtoTest tracing stays enabled and independent: the exported spans do not replace `.prototrace`. Large structured values (`context.value`, `observation.data`, `shape.*`, …) stay only in the archive, and attributes longer than 2048 characters are dropped from exported spans.

## Learn more

- [OpenTelemetry guide](https://prototest.dev/docs/observability/opentelemetry)
- [OpenTelemetry instrumentation tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.OpenTelemetry.Tests/OpenTelemetryInstrumentationTests.cs)
