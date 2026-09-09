# Extension points reference

Choose an extension point by the behavior you need to add:

| I want to… | Use | Registered or applied through |
| --- | --- | --- |
| Configure services or application configuration | `IProtoHostBuilder` | `ConfigureServices` / `ConfigureAppConfiguration` |
| Run once before or after the suite | `IProtoRunHook` | `AddRunHook<T>()` |
| Run setup or cleanup around every test | `IProtoTestHook` | `AddTestHook<T>()` |
| Declare behavior on a class or method | `ProtoAttribute` | Attribute usage |
| Create a named test-scoped resource | `IProtoClientInitializer` | Dependency injection |
| Share typed state in one test | `IProtoContext` | `Proto.Context.SetContext` |
| Publish test artifacts | `IProtoTestAttachmentPublisher` | Adapter lifecycle or a custom host integration |
| Receive execution events | `IProtoCollector` | `IProtoTargetBuilder.WithCoverage<T>()` or DI |
| Aggregate standard coverage items | `ProtoCollector` | Derive a collector |
| Export coverage items | `IProtoSink` | Sink/reporting integration |

## Host builder

`IProtoHostBuilder` provides:

```csharp
builder
	.ConfigureServices(services => { })
	.ConfigureAppConfiguration(configuration => { })
	.ConfigureTestIds(options => { })
	.AddRunHook<RunHook>()
	.AddTestHook<TestHook>();
```

Integration packages add their own fluent methods to this builder, such as `AddRest` and `AddAspNetCoreServer`.

## Hooks and attributes

Use `IProtoRunHook` for suite-level resources, `IProtoTestHook` for behavior around every test, and `ProtoAttribute` for behavior selected on a class or method. See [hooks](../extending/hooks.md).

## Client initializers

Implement `IProtoClientInitializer` when an extension needs to create and register a named client for each test context. See [custom attributes and clients](../extending/custom-attributes-and-clients.md).

## Collectors and sinks

Implement `IProtoCollector` when an extension consumes `CoverageHit` values and produces `CoverageItem` values. Derive from `ProtoCollector` when its standard aggregation is suitable. Implement `IProtoSink` when coverage items need to be exported.

See [coverage extensions](../extending/coverage.md).

## Test attachments

Tests and integrations register artifacts through `ProtoExecutionContext`. Framework adapters implement `IProtoTestAttachmentPublisher` to transfer those artifacts during teardown. See [test attachments](test-attachments.md).
