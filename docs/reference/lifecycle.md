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
await host.StartTestAsync(testName, testId, methodInfo, attributes);

try
{
	// The test framework invokes the test method.
}
finally
{
	await host.CompleteTestAsync(attributes);
}
```

### Start order

`StartTestAsync`:

1. Creates the scoped dependency-injection scope.
2. Establishes the ambient `Proto.Context`.
3. Runs each registered `IProtoClientInitializer`.
4. Runs `IProtoTestHook.BeforeTestAsync` in ascending `Order`.
5. Runs class and method `ProtoAttribute.BeforeTestAsync` in ascending `Order`.

If setup fails, the context is disposed and the ambient context is cleared.

### Completion order

`CompleteTestAsync`:

1. Runs class and method `ProtoAttribute.AfterTestAsync` in descending `Order`.
2. Runs `IProtoTestHook.AfterTestAsync` in descending `Order`.
3. Disposes registered clients and the dependency-injection scope.
4. Clears the ambient `Proto.Context`.

Completion cleanup is idempotent and can be called after setup failure.

## Ordering rule

Lower order values run earlier during setup and later during teardown:

```text
Before: lower order -> higher order -> test body
After:  test body -> higher order -> lower order
```

## Disposal

The test context owns clients registered through `RegisterClient<TClient>`. Clients are disposed in reverse registration order. Cleanup continues after an individual disposal failure so remaining resources still receive cleanup.

Registering the same client type and name twice is rejected instead of silently replacing an owned resource.

## Ambient context

`Proto.Context` is scoped to the current asynchronous test flow. Do not store it or a test-scoped client in static state, and do not reuse it across tests.

See [execution context](execution-context.md) for the available context APIs.
