# Execution tracing

ProtoTest.Core automatically records a portable execution trace for every run. The trace explains what ProtoTest did around each test: setup, integration operations, failures, rollback, teardown, and disposal.

At the end of a normal run, Core writes `TestResults/prototest-{runId}.prototrace`. Open this file in the static ProtoTrace viewer. The viewer processes the archive entirely inside the browser; it does not upload the trace.

## Trace and observations are different

Tracing is automatic technical execution history. Observations are deliberately emitted semantic facts consumed by collectors for coverage and reporting.

```text
execution -> ProtoTrace -> .prototrace -> execution viewer

semantic fact -> ProtoObservation -> collector -> report items -> report sink -> run artifact
```

An integration may produce both during one operation, but neither model is converted into the other. Generated sink files can be bundled in ProtoTrace as run-level artifacts without turning their report data back into trace events.

## Configuration

Complete tracing is enabled by default. Change the output path through the host builder:

```csharp
builder.ConfigureTracing(options =>
{
    options.OutputPath = "TestResults/integration.prototrace";
});
```

ProtoTest always records the complete execution: client initialization and resolution, hooks, attributes, context setup, authentication selection and application, request construction, assertions, observations, attachments, disposal, rollback, and failures. Presentation choices such as collapsed operations and contextual tabs belong to the viewer and never remove data from the archive.

Structured context and observation values are serialized into diagnostic attributes. Shape assertions include the expected and actual JSON, every successfully matched path, and structured mismatches containing the path, reason, expected value, and actual value. JSON diagnostic values are limited to 64 KiB and properties with common secret names are redacted by default. A trace can still contain application data and should be handled as a test artifact.

Tracing can be explicitly disabled for a specialized host:

```csharp
builder.ConfigureTracing(options => options.Enabled = false);
```

Request and response bodies, screenshots, and similar sensitive or large artifacts remain governed by their integration-specific capture settings. Enabling the execution trace does not automatically enable body or screenshot capture. When an integration registers one through `AddAttachment`, the file or in-memory content is stored once under `resources/` in the `.prototrace` archive. The timeline retains only the registration and publication metadata. Sinks implementing `IProtoSinkArtifactSource` similarly expose their generated files as run-level artifacts; the built-in JSON and HTML reporting sinks do this automatically. The viewer can preview text, JSON, XML, HTML, PDF, images, audio and video, and download every bundled media type.

A test reported as passed by its framework becomes `Partial` when its trace contains a real failed operation or event. The concrete diagnostic remains `Failed`, while its successful parent execution and test result are promoted to `Partial`. This surfaces captured non-blocking failures without incorrectly claiming that the test runner failed the test.

## OpenTelemetry and Sentry

Core emits every traced operation through the standard .NET `ActivitySource` named `ProtoTest`, without taking a dependency on an observability SDK. Add `ProtoTest.OpenTelemetry` when an application already has an OpenTelemetry tracing pipeline:

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using ProtoTest.OpenTelemetry;

using var provider = Sdk.CreateTracerProviderBuilder()
    .AddProtoTestInstrumentation()
    .AddOtlpExporter()
    .Build();
```

The local `.prototrace` remains the complete, unsampled diagnostic artifact. External spans deliberately contain compact searchable attributes; large context values, shape payloads, and observation data stay local.

Sentry support uses Sentry's official `Sentry.OpenTelemetry` bridge:

```csharp
using var sentry = SentrySdk.Init(options => options.UseOpenTelemetry());
using var provider = Sdk.CreateTracerProviderBuilder()
    .AddProtoTestInstrumentation()
    .AddSentry()
    .Build();
```

There is intentionally no `ProtoTest.Sentry` package: it would only wrap and version-couple the official bridge. The package is named `ProtoTest.OpenTelemetry`, rather than `ProtoTest.Tracing.OpenTelemetry`, because the tracing model and recorder are already first-class Core APIs and the package adds one external integration.

## Integration authors

Integration packages write operations through `ProtoExecutionContext.Trace`:

```csharp
using var operation = context.Trace.StartOperation(
    "queue.publish",
    "Queue · Publish order-created",
    "ProtoTest.Queues");

try
{
    await PublishAsync();
    operation.Succeed();
}
catch (Exception exception)
{
    operation.Fail(exception);
    throw;
}
```

Use stable machine-readable kinds and separate human-readable names. The first segment of the kind is treated as its namespace; the viewer automatically creates a category for namespaces it has not seen before. Put compact searchable facts in attributes and encode structured diagnostic values as JSON. Trace failures must never replace the original integration or test exception.
