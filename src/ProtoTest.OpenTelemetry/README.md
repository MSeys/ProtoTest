# ProtoTest.OpenTelemetry

Registers ProtoTest's built-in `ActivitySource` with an OpenTelemetry tracing pipeline. The local `.prototrace` archive remains enabled and independent.

Install an exporter such as `OpenTelemetry.Exporter.OpenTelemetryProtocol` alongside this package when needed.

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using ProtoTest.OpenTelemetry;

using var provider = Sdk.CreateTracerProviderBuilder()
    .AddProtoTestInstrumentation()
    .AddOtlpExporter()
    .Build();
```

ProtoTest operations become spans and instantaneous trace entries become span events. Large structured values remain in `.prototrace` and are not added to exported span attributes.

## Sentry

Use Sentry's official OpenTelemetry integration with the same source:

```csharp
using var sentry = SentrySdk.Init(options => options.UseOpenTelemetry());
using var provider = Sdk.CreateTracerProviderBuilder()
    .AddProtoTestInstrumentation()
    .AddSentry()
    .Build();
```

This keeps Sentry SDK configuration and versioning owned by Sentry instead of duplicating it in ProtoTest.
