# ProtoTest.OpenTelemetry

Adds ProtoTest's `ActivitySource` to an OpenTelemetry tracing pipeline.

```bash
dotnet add package ProtoTest.OpenTelemetry
```

Call `AddProtoTestInstrumentation()` on your `TracerProviderBuilder`. ProtoTest operations become spans and trace events become span events.

This package does not include an exporter and does not replace the local `.prototrace` file. Exporters, sampling and OpenTelemetry resources remain part of the application's OpenTelemetry setup.

## Learn more

- [OpenTelemetry](https://prototest.dev/docs/observability/opentelemetry)
- [ProtoTrace](https://prototest.dev/docs/observability/prototrace)
- [Package source](https://github.com/MSeys/ProtoTest/tree/main/src/ProtoTest.OpenTelemetry)
