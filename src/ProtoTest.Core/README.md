# ProtoTest.Core

The shared, runner-independent runtime at the centre of the ProtoTest foundation: host and lifecycle,
an isolated execution context per test, hooks and attributes, clients, owned resources and tracing.

```bash
dotnet add package ProtoTest.Core
```

Most test projects install a runner adapter such as `ProtoTest.NUnit` or `ProtoTest.Xunit3`, which brings Core in transitively.

## Quick start

```csharp
public sealed class EnvironmentContext(Uri baseUri) : IProtoContext
{
    public Uri BaseUri { get; } = baseUri;
}

public sealed class EnvironmentHook : IProtoTestHook
{
    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        context.SetContext(new EnvironmentContext(new Uri("https://localhost:5099")));
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}

await using var host = new ProtoHostBuilder()
    .AddTestHook<EnvironmentHook>()
    .ConfigureTracing(trace => trace.OutputPath = "TestResults/run.prototrace")
    .AddRunGate("no error findings", context => context
        .ItemsOfKind(ProtoReportItemKinds.Finding)
        .Any(item => item.Status == ProtoReportStatus.Error)
        ? ProtoRunGateResult.Failed("The run recorded error findings.")
        : ProtoRunGateResult.Passed("No error findings were recorded."))
    .Build();

var testMethod = typeof(EnvironmentTests).GetMethod(nameof(EnvironmentTests.ReadsTheEnvironment))!;

await host.StartAsync();
_ = await host.StartTestAsync("Reads the environment", testMethod);

// Inside a test, Proto.Context is the active ProtoExecutionContext.
var environment = Proto.Context.Resolve<EnvironmentContext>();
Proto.Context.AddAttachment("environment.txt", environment.BaseUri.ToString());
```

## What it adds

- **Host builder** — `ConfigureServices`, `ConfigureAppConfiguration`, `ConfigureTestIds`, `ConfigureTracing`, `AddTestHook<THook>`, `AddRunHook<TRunHook>`, `AddRunGate`, `AddResource` and `Build()`.
- **Execution context** — `Proto.Context` exposes the test identity, `Resolve<T>`/`SetContext<T>`, `Client<T>(name)`, `RegisterResource`, `AddAttachment`, `AddFinding`, `RecordObservation` and `Trace`.
- **Applications** — `AddApplication(name, app => …)` declares an application and its clients; `AddCapability` and `ProtoHost.HasCapability` back `[RequiresCapability]`.
- **Skip conditions** — `[RequiresCapability(kind, CapabilityName = …)]` and `[RequiresInProcess]`; adapters evaluate them before `StartTestAsync`.
- **Tracing** — the lifecycle writes `test.setup`, `test.execution` and `test.teardown`; entities, resources, findings and gates are recorded automatically. `ProtoTraceOptions` controls `Enabled`, `OutputPath`, `ActivitySources`, `CaptureSourceLocations` and `EmbedSources`.

One `ProtoExecutionContext` exists per async flow, one host per builder, and `AddTestHook`/`AddRunHook`/`AddRunGate` do not dedupe repeated calls.

## Learn more

- [Foundation overview](https://prototest.dev/docs/foundation/overview)
- [Demo setup](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
