# Test framework adapters

ProtoTest provides adapters for NUnit, xUnit, xUnit v3, MSTest, and TUnit. Install the adapter matching the test framework used by the project:

| Framework | Package |
| --- | --- |
| NUnit | `ProtoTest.NUnit` |
| xUnit | `ProtoTest.Xunit` |
| xUnit v3 | `ProtoTest.Xunit3` |
| MSTest | `ProtoTest.MSTest` |
| TUnit | `ProtoTest.TUnit` |

The adapter is responsible for translating the framework's setup and teardown callbacks into the common `StartTestAsync` / `CompleteTestAsync` lifecycle. Test code, hooks, attributes, context state, and integration configuration remain the same across adapters.

## NUnit example

```csharp
using ProtoTest.Core;
using ProtoTest.NUnit;

[SetUpFixture]
public sealed class TestSetup : ProtoTestAssembly
{
	protected override void Configure(IProtoHostBuilder builder)
	{
		// Register integrations and hooks here.
	}
}

public sealed class OrderTests
{
	[ProtoTest]
	public Task HasAnExplicitLifecycle()
	{
		return Task.CompletedTask;
	}
}
```

The unified demo uses NUnit with eight parallel workers and is a runnable reference: [`samples/ProtoTest.Demo`](../../samples/ProtoTest.Demo).

The adapter choice changes the test framework annotations, not the ProtoTest model. After choosing an adapter, continue with [writing tests](../guides/writing-tests.md), [hooks](../extending/hooks.md), and [context and state](../guides/context-and-state.md).

## MSTest

MSTest requires explicit assembly initialization and cleanup. Cleanup is important because it runs `AfterRun` hooks and exports reports.

```csharp
[TestClass]
public sealed class TestSetup : ProtoTestAssembly
{
	[AssemblyInitialize]
	public static Task Initialize(TestContext _) =>
		InitializeAsync(builder =>
		{
			// Register integrations and hooks.
		});

	[AssemblyCleanup]
	public static Task Cleanup() => CleanupAsync();
}

[TestClass]
public sealed class OrderTests
{
	[ProtoTest]
	public async Task Scenario() { }
}
```

## TUnit

Register the ProtoTest executor once and connect a `ProtoTestAssembly` to TUnit's assembly hooks. Tests keep TUnit's normal `[Test]` attribute.

```csharp
using ProtoTest.TUnit;
using TUnit.Core.Executors;

[assembly: TestExecutor<ProtoTestExecutor>()]

public sealed class TestSetup : ProtoTestAssembly
{
	[Before(Assembly)]
	public static Task Initialize(AssemblyHookContext _) =>
		InitializeAsync(builder => { });

	[After(Assembly)]
	public static Task Cleanup(AssemblyHookContext _) => CleanupAsync();
}
```

## xUnit.net v2

xUnit.net v2 uses a collection fixture. Every ProtoTest class must belong to that collection and combines `[Fact]` or `[Theory]` with `[ProtoTest]`.

```csharp
public sealed class TestSetup : ProtoTestAssembly
{
	protected override void Configure(IProtoHostBuilder builder) { }
}

[CollectionDefinition(Name)]
public sealed class ProtoTestCollection : ICollectionFixture<TestSetup>
{
	public const string Name = "ProtoTest";
}

[Collection(ProtoTestCollection.Name)]
public sealed class OrderTests
{
	[Fact, ProtoTest]
	public async Task Scenario() { }
}
```

## xUnit.net v3

xUnit.net v3 supports an assembly fixture and native attachments. Use the combined ProtoTest fact and theory attributes.

```csharp
using ProtoTest.Xunit3;

[assembly: AssemblyFixture(typeof(TestSetup))]

public sealed class TestSetup : ProtoTestAssembly
{
	protected override void Configure(IProtoHostBuilder builder) { }
}

public sealed class OrderTests
{
	[ProtoTestFact]
	public async Task Scenario() { }

	[ProtoTestTheory]
	[InlineData(42)]
	public async Task ScenarioWithData(int id) { }
}
```

The adapter changes only the framework lifecycle and annotations. Test bodies, `Proto.Context`, hooks, attributes, clients, and collectors remain shared.
