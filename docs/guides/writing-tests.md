# Writing tests with ProtoTest

A ProtoTest method is still an ordinary test method. The `[ProtoTest]` attribute asks the adapter to create the test context before the method and clean it up afterward. Inside the method, use `Proto.Context` to access the resources configured for the test. The snippets below focus on the relevant test code; the complete package and suite setup is in the [first-test guide](../getting-started/first-test.md).

## The basic shape

The following snippet assumes a named `Orders` client was registered during suite setup. For REST setup, see [clients and attributes](clients-and-attributes.md).

```csharp
using System.Net;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.NUnit;

[ProtoTest]
public async Task GetOrder_ReturnsExpectedOrder()
{
	var client = Proto.Context.Client<HttpClient>("Orders");
	using var response = await client.GetAsync("/orders/42");

	Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
}
```

Integration packages usually provide a more expressive facade. For REST:

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[ProtoTest]
[RestClient("Orders")]
public async Task GetOrder_ReturnsExpectedOrder()
{
	using var response = await Proto.Context.Rest()
		.GetAsync("/orders/{id}", new { id = 42 });

	response
		.ShouldHaveStatus(HttpStatusCode.OK)
		.ShouldMatchShape(new
		{
			id = 42,
			status = "confirmed"
		});
}
```

## What is available through `Proto.Context`?

`Proto.Context` is the current `ProtoExecutionContext`. Common operations include:

| Operation | Use |
| --- | --- |
| `Client<T>(name)` | Get a named client registered by an integration or initializer. |
| `TryClient<T>(name)` | Get a named client when it may not be registered. |
| `Service<T>()` | Resolve a required service from the test scope. |
| `TryService<T>()` | Resolve an optional service from the test scope. |
| `Context<T>()` | Get required custom state. |
| `TryContext<T>()` | Get optional custom state. |
| `SetContext(value)` | Register or replace custom state. |
| `RecordObservation(...)` | Send integration or test information to matching collectors. |

## Keep test intent in the method

Use suite configuration and hooks for repeated infrastructure. Keep the test method focused on the behavior under test:

```csharp
[ProtoTest]
[RestClient("Orders")]
public async Task CreateOrder_ReturnsCreatedOrder()
{
	using var response = await Proto.Context.Rest()
		.Body(new { product = "notebook", quantity = 2 })
		.PostAsync("/orders");

	response
		.ShouldHaveStatus(HttpStatusCode.Created)
		.ShouldMatchShape(new
		{
			id = 43,
			status = JsonValue.Regex("^(confirmed|pending)$")
		});
}
```

Avoid storing `Proto.Context` or a test-scoped client in static fields. The context belongs to the current asynchronous test flow and is disposed when the test completes.

## Use class-level configuration

If all methods in a class use the same client or authentication, put the attributes on the class:

```csharp
[RestClient("Orders")]
[Auth<BearerTokenAuthenticator>("orders-token")]
public sealed class OrderTests
{
	[ProtoTest]
	public Task GetsAnOrder() => Task.CompletedTask;

	[ProtoTest]
	public Task CreatesAnOrder() => Task.CompletedTask;
}
```

A method-level attribute overrides the inherited class-level setting for that test.

## Assertions and failures

Integration response assertions include diagnostic response content in their failures. Prefer those assertions when available, because they preserve the protocol details that matter during a failed test. For REST, use `ShouldHaveStatus`, `ReadAsJson<T>`, and `ShouldMatchShape`.

## Next steps

- Store reusable typed data with [context and state](context-and-state.md).
- Configure [clients and attributes](clients-and-attributes.md).
- Add suite or test behavior with [hooks](../extending/hooks.md).
