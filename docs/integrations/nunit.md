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

For framework-specific details, inspect the adapter projects under [`src`](../../src). The REST demo uses NUnit and is a runnable reference: [`samples/ProtoTest.Rest.Demo`](../../samples/ProtoTest.Rest.Demo).

The adapter choice changes the test framework annotations, not the ProtoTest model. After choosing an adapter, continue with [writing tests](../guides/writing-tests.md), [hooks](../extending/hooks.md), and [context and state](../guides/context-and-state.md).

## Other frameworks

The same Core and integration setup applies to the other adapters. Install the matching package from the table above, then replace the NUnit fixture and test annotations with the annotations required by that framework. The test body, `Proto.Context`, hooks, attributes, state, clients, and collectors remain the same.
