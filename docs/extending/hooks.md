# Extend the lifecycle with hooks

Hooks are the main way to add reusable behavior without putting infrastructure code in every test method. The examples focus on the extension itself; register them from the suite setup shown in the [first-test guide](../getting-started/first-test.md).

ProtoTest has two hook scopes:

| Hook | Runs | Typical use |
| --- | --- | --- |
| `IProtoRunHook` | Once before the suite and once after the suite | Start or stop an external dependency, create a shared environment, publish a final report. |
| `IProtoTestHook` | Before and after every test | Seed test state, open a transaction, add diagnostics, clean up per-test resources. |

Both hooks are registered through `IProtoHostBuilder` and are resolved from dependency injection.

## Run hooks

Use `IProtoRunHook` for suite-level work:

```csharp
public sealed class EnvironmentHook : IProtoRunHook
{
	public int Order => 10;

	public async Task BeforeRunAsync(CancellationToken cancellationToken = default)
	{
		// Start a shared dependency or prepare the test environment.
		await Task.CompletedTask;
	}

	public async Task AfterRunAsync(CancellationToken cancellationToken = default)
	{
		// Stop the dependency and publish suite-level results.
		await Task.CompletedTask;
	}
}
```

Register it once:

```csharp
protected override void Configure(IProtoHostBuilder builder)
{
	builder.AddRunHook<EnvironmentHook>();
}
```

A run hook does not receive a `ProtoExecutionContext`, because it runs outside an individual test context. Use it for suite resources rather than per-test state.

## Test hooks

Use `IProtoTestHook` for behavior around every test:

```csharp
public sealed class ScenarioHook : IProtoTestHook
{
	public int Order => 100;

	public Task BeforeTestAsync(ProtoExecutionContext context)
	{
		context.SetContext(new ScenarioState());
		return Task.CompletedTask;
	}

	public Task AfterTestAsync(ProtoExecutionContext context)
	{
		var state = context.TryContext<ScenarioState>();
		// Record diagnostics or release per-test resources.
		return Task.CompletedTask;
	}
}

public sealed class ScenarioState : IProtoContext
{
	public List<string> Events { get; } = [];
}
```

Register it with:

```csharp
builder.AddTestHook<ScenarioHook>();
```

The hook and the test access the same `ProtoExecutionContext`, so this is a natural place to prepare [shared typed state](../guides/context-and-state.md).

## Ordering

Lower `Order` values run earlier during setup and later during teardown. This gives the following behavior:

```text
Before:  lower order -> higher order -> test body
After:   test body -> higher order -> lower order
```

Use ordering when one hook depends on another. Keep the numbers meaningful and avoid relying on registration order.

## Dependency injection

Hooks can use constructor injection:

```csharp
public sealed class DatabaseHook(IDatabase database) : IProtoTestHook
{
	public async Task BeforeTestAsync(ProtoExecutionContext context)
	{
		await database.BeginScenarioAsync();
	}

	public async Task AfterTestAsync(ProtoExecutionContext context)
	{
		await database.EndScenarioAsync();
	}
}
```

Register supporting services through `ConfigureServices`:

```csharp
builder.ConfigureServices(services =>
{
	services.AddSingleton<IDatabase, Database>();
});
```

## Choose the right extension point

- Use a run hook for one-time suite infrastructure.
- Use a test hook for repeated per-test setup and teardown.
- Use a `ProtoAttribute` when behavior should be selected declaratively on a class or method.
- Use a client initializer when an integration needs to create a named client in each test context.
- Use a collector when an integration emits coverage hits.
