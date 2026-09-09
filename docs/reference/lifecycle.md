# Lifecycle reference

This page describes the lifecycle implemented by the framework adapters. For practical usage, start with [writing tests](../guides/writing-tests.md) or [hooks](../extending/hooks.md).

## Suite lifecycle

A test assembly creates and owns one configured `ProtoHost`. `IProtoRunHook` instances run once around the suite:

```text
BeforeRunAsync
  -> all tests
AfterRunAsync
```

Register a run hook with:

```csharp
builder.AddRunHook<EnvironmentRunHook>();
```

Run hooks do not receive a `ProtoExecutionContext`, because they run outside an individual test.

## Test lifecycle

Framework adapters translate their setup and teardown callbacks into:

```csharp
await host.StartTestAsync(testName, methodInfo, attributes);

try
{
	// The test framework invokes the test method.
}
finally
{
	await host.CompleteTestAsync();
}
```

The host retains the resolved attribute instances from `StartTestAsync` and reuses them during completion. This allows an attribute instance to keep lifecycle state between its `BeforeTestAsync` and `AfterTestAsync` calls.

### Start order

`StartTestAsync`:

1. Creates the scoped dependency-injection scope.
2. Establishes the ambient `Proto.Context`.
3. Groups registered `IProtoClientInitializer` instances by client type and name, then tries each group in dependency-injection registration order until one initializer succeeds.
4. Runs `IProtoTestHook.BeforeTestAsync` in ascending `Order`.
5. Runs class and method `ProtoAttribute.BeforeTestAsync` in ascending `Order`.

If setup fails, components whose `Before` method completed are rolled back in reverse order. Cleanup continues if a rollback callback fails, after which the context is disposed and the ambient context is cleared.

### Completion order

`CompleteTestAsync`:

1. Runs class and method `ProtoAttribute.AfterTestAsync` in descending `Order`.
2. Runs `IProtoTestHook.AfterTestAsync` in descending `Order`.
3. Disposes registered clients and the dependency-injection scope.
4. Clears the ambient `Proto.Context`.

Completion cleanup is idempotent and can be called after setup failure.
All teardown callbacks are attempted. A single failure is rethrown directly; multiple failures are reported together as an `AggregateException`.

## Ordering rule

Lower order values run earlier during setup and later during teardown:

```text
Before: lower order -> higher order -> test body
After:  test body -> higher order -> lower order
```

Hooks and attributes are separate lifecycle phases: all hooks run before all attributes during setup, and all attributes run before all hooks during teardown. `Order` sorts components only within their own phase.

## Disposal

The test context owns clients registered through `RegisterClient<TClient>`. Clients are disposed in reverse registration order. Cleanup continues after an individual disposal failure so remaining resources still receive cleanup.

Registering the same client type and name twice is rejected instead of silently replacing an owned resource.
Registration is also rejected after context disposal has begun.

## Ambient context

`Proto.Context` is scoped to the current asynchronous test flow. Do not store it or a test-scoped client in static state, and do not reuse it across tests.

Multiple hosts can exist in one process. Inside a test, `Proto.Host` resolves the host that owns the current context. Outside a test it is available only when exactly one host is active.

See [execution context](execution-context.md) for the available context APIs.
