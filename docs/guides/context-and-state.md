# Sharing context and state

`ProtoExecutionContext` is more than a client registry. It is the per-test place where infrastructure and typed scenario state can meet.

Use `IProtoContext` for data that should be created in one part of the lifecycle and consumed somewhere else:

- a run or test hook can prepare it;
- a client initializer can add data after creating a client;
- the test can read or update it;
- a collector or after-hook can inspect it before the test context is disposed.

## Define typed state

A context object is a normal class or record implementing `IProtoContext`:

```csharp
public sealed class OrderScenario : IProtoContext
{
	public string? OrderId { get; set; }
	public decimal ExpectedTotal { get; init; }
	public List<string> Events { get; } = [];
}
```

The marker interface keeps contextual state distinct from services and named clients.

## Register state in a test hook

A test hook can create state before each test:

```csharp
public sealed class OrderScenarioHook : IProtoTestHook
{
	public int Order => 100;

	public Task BeforeTestAsync(ProtoExecutionContext context)
	{
		context.SetContext(new OrderScenario
		{
			ExpectedTotal = 129.95m
		});

		return Task.CompletedTask;
	}

	public Task AfterTestAsync(ProtoExecutionContext context)
	{
		var scenario = context.TryContext<OrderScenario>();
		if (scenario != null)
		{
			Console.WriteLine($"Events recorded: {scenario.Events.Count}");
		}

		return Task.CompletedTask;
	}
}
```

Register the hook during suite configuration:

```csharp
protected override void Configure(IProtoHostBuilder builder)
{
	builder.AddTestHook<OrderScenarioHook>();
}
```

## Consume and update state in the test

The test can require the state with `Context<T>()`:

```csharp
[ProtoTest]
public async Task GetOrder_RecordsScenarioData()
{
	var scenario = Proto.Context.Context<OrderScenario>();
	scenario.Events.Add("request-started");

	var response = await Proto.Context.Rest("Orders")
		.GetAsync("/orders/{id}", new { id = 42 });

	response.ShouldHaveStatus(HttpStatusCode.OK);
	scenario.Events.Add("response-validated");
}
```

`Context<T>()` throws a clear `InvalidOperationException` when the state has not been registered. Use `TryContext<T>()` when the state is optional:

```csharp
var scenario = Proto.Context.TryContext<OrderScenario>();
if (scenario != null)
{
	scenario.Events.Add("optional-observation");
}
```

Registering another instance of the same type replaces the previous instance for the current test. State is not shared between test contexts.

## Access state from hooks and collectors

Hooks receive the same `ProtoExecutionContext` instance that the test uses:

```csharp
public Task AfterTestAsync(ProtoExecutionContext context)
{
	var scenario = context.Context<OrderScenario>();
	// Publish scenario.OrderId or inspect scenario.Events.
	return Task.CompletedTask;
}
```

Collectors receive `CoverageHit` values, not the context directly. If a collector needs scenario metadata, include it in the hit's data or have the integration record a typed value before dispatching the hit. A collector can also resolve scoped services from its constructor when the collector is registered through dependency injection.

The important boundary is that state belongs to the active test context. Do not use static mutable state to move scenario data between tests.

## State versus services and clients

Use a service when the object is application or infrastructure behavior registered through dependency injection. Use a named client when the object is an external or application endpoint client. Use `IProtoContext` when the object is mutable scenario data that should travel between the test, lifecycle hooks, and integration code.

| Need | Use |
| --- | --- |
| Resolve application behavior | `Proto.Context.Service<T>()` |
| Access an initialized endpoint client | `Proto.Context.Client<T>(name)` |
| Share current test scenario data | `Proto.Context.Context<T>()` |
| Record protocol or contract execution | `Proto.Context.RecordHit(...)` |

## Next steps

- Learn how [hooks and attributes](../extending/hooks.md) participate in the lifecycle.
- Build [custom clients and initializers](../extending/custom-attributes-and-clients.md).
- See the [Core lifecycle reference](../concepts/core-lifecycle.md) for ordering and disposal behavior.
