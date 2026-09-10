# Execution context reference

`ProtoExecutionContext` is the per-test container for services, named clients, typed scenario state, and recorded observations. The ambient gateway is `Proto.Context`.

## Services

```csharp
var required = Proto.Context.Service<IOrderService>();
var optional = Proto.Context.TryService<IOptionalService>();
```

Services are resolved from the test's dependency-injection scope.

## Named clients

```csharp
var client = Proto.Context.Client<HttpClient>("Orders");
var optionalClient = Proto.Context.TryClient<HttpClient>("Optional");
```

Clients are registered by integrations or `IProtoClientInitializer` implementations:

```csharp
context.RegisterClient(client, "Orders");
```

The active context owns registered clients and disposes them during completion.

## Typed state

Implement `IProtoContext` for state that should move between a hook, test, integration, or collector:

```csharp
public sealed record ScenarioState(string Phase) : IProtoContext;

context.SetContext(new ScenarioState("prepared"));
var state = context.Context<ScenarioState>();
var optionalState = context.TryContext<ScenarioState>();
```

`Context<T>()` throws when the type has not been registered. `TryContext<T>()` returns `null`. Registering another instance of the same type replaces the current value for that test.

See [context and state](../guides/context-and-state.md) for the complete usage pattern.

## Observations

```csharp
Proto.Context.RecordObservation(
	targetName: "Orders",
	kind: "http.response",
	identifier: "GET /orders/{id}",
	data: responseData);

var observations = Proto.Context.RecordedObservations;
```

Matching collectors receive recorded observations. Most integration code records observations automatically.

## Test metadata

The context also exposes metadata for the active test:

```csharp
var name = Proto.Context.TestName;
var id = Proto.Context.TestId;
var number = Proto.Context.TestNumber;
var method = Proto.Context.TestMethod;
var configuration = Proto.Context.Configuration;
```

Use metadata when hooks, clients, or collectors need to identify the current test or resolve configuration.

`TestId` contains only decimal digits and is convenient for composing unique test data. `TestNumber` exposes the same value as a `long`; `Id` exposes the complete `ProtoTestId` value. IDs are guaranteed unique within one host. Configure a run or CI-worker prefix when data must also remain unique across hosts or processes:

```csharp
builder.ConfigureTestIds(options =>
{
	options.RunPrefix = 482731; // For example, a numeric CI build ID
	options.SequenceDigits = 6;
});
```

Without an explicit prefix, each host uses a random six-digit run prefix.
