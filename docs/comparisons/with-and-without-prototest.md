# The same REST test with and without ProtoTest

ProtoTest is short for **prototype testing**: making integration tests as simple, readable, and frictionless as prototyping. It is not about testing software prototypes, and it is not required for every integration test. Its value appears when setup, client ownership, authentication, response matching, and coverage are repeated across a suite.

This comparison uses the same scenario as the REST demo: call `GET /orders/42`, authenticate with a bearer token, and verify the response.

## Without ProtoTest

A plain NUnit test can do the work directly. The test owns the server URL, creates the `HttpClient`, adds authentication, sends the request, reads the body, and performs assertions.

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NUnit.Framework;

[Test]
public async Task GetOrder_ReturnsExpectedOrder()
{
	using var client = new HttpClient
	{
		BaseAddress = new Uri(TestServerUrl)
	};

	client.DefaultRequestHeaders.Authorization =
		new AuthenticationHeaderValue("Bearer", "orders-token");

	using var response = await client.GetAsync("/orders/42");

	Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

	var body = await response.Content.ReadFromJsonAsync<Order>();

	Assert.That(body, Is.Not.Null);
	Assert.That(body!.Id, Is.EqualTo(42));
	Assert.That(body.Status, Is.EqualTo("confirmed"));
	Assert.That(body.Total, Is.EqualTo(129.95m));
}
```

This is a valid test. As the suite grows, the same concerns commonly spread into every test or into custom fixtures:

- the API server and base URL must be configured consistently;
- clients must be created and disposed consistently;
- authentication setup is repeated or hidden in fixture state;
- JSON deserialization requires a response DTO for each assertion shape;
- route and response coverage needs separate instrumentation.

## With ProtoTest

ProtoTest moves suite-level setup and test-scoped ownership into the host and context. Common attributes can also be placed on the test class, so the client and authentication are declared once for every test in that class:

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[RestClient("Orders")]
[BearerToken("orders-token")]
public sealed class OrderApiTests
{
	[ProtoTest]
	public async Task GetOrder_ReturnsExpectedOrder()
	{
		var response = await Proto.Context.Rest()
			.GetAsync("/orders/{id}", new { id = 42 });

		response
			.ShouldHaveStatus(HttpStatusCode.OK)
			.ShouldMatchShape(new
			{
				id = 42,
				status = "confirmed",
				total = 129.95m
			});
	}

	[ProtoTest]
	public async Task CreateOrder_ReturnsCreatedOrder()
	{
		var response = await Proto.Context.Rest()
			.Body(new { product = "notebook", quantity = 2 })
			.PostAsync("/orders");

		response.ShouldHaveStatus(HttpStatusCode.Created);
	}
}
```

The class-level `[RestClient]` and `[BearerToken]` apply to both methods. A method can still override either attribute when it targets another client or requires different credentials:

```csharp
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[RestClient("Orders")]
[BearerToken("orders-token")]
public sealed class MixedApiTests
{
	[ProtoTest]
	[RestClient("Inventory")]
	[BearerToken("inventory-token")]
	public Task InventoryTest_OverridesClassDefaults()
	{
		return Task.CompletedTask;
	}
}
```

For a one-off test, attributes can remain at method level:

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[ProtoTest]
[RestClient("Orders")]
[BearerToken("orders-token")]
public async Task GetOneOrder_WithMethodConfiguration()
{
	var response = await Proto.Context.Rest()
		.GetAsync("/orders/{id}", new { id = 42 });

	response.ShouldHaveStatus(HttpStatusCode.OK);
}
```

The class-level form keeps the test body focused on behavior while eliminating repeated client and authentication declarations.

The suite setup registers the named client and its collectors once:

```csharp
protected override void Configure(IProtoHostBuilder builder)
{
	builder
		.ConfigureAppConfiguration(configuration =>
		{
			// Supply the external API's base URL here.
		})
		.AddRest(rest => rest
			.AddClient("Orders")
			.WithCoverage<RestCoverageCollector>());
}
```

## What changed?

| Concern | Plain NUnit | ProtoTest |
| --- | --- | --- |
| Test framework | NUnit owns setup and teardown | `ProtoTest.NUnit` translates callbacks to the common lifecycle |
| Client setup | Test or fixture creates `HttpClient` | `AddRest` registers a named client |
| Authentication | Header configured manually | `[BearerToken]` or fluent `.Auth(...)` |
| Route parameters | URL assembled manually | Route template plus anonymous parameters |
| JSON verification | Deserialize into a DTO or inspect JSON | `ShouldMatchShape` verifies the described shape |
| Coverage | Requires separate instrumentation | REST requests record hits; collectors are composed with `.WithCoverage<T>()` |
| Cleanup | Test author owns `HttpClient` and fixture state | Context disposes clients in reverse registration order and completes teardown |
| Reuse | Custom fixture conventions | Shared hooks, attributes, named clients, and configuration |

## When to choose which style

Use a plain test when the scenario is small, setup is local, and introducing a lifecycle abstraction would add more structure than value.

Use ProtoTest when multiple tests share clients or environments, when several integration types need one lifecycle, when test-scoped resources need deterministic cleanup, or when protocol/contract coverage should be collected without changing every assertion.

## See the runnable version

The comparison snippets are intentionally focused on the test boundary. The complete ProtoTest implementation uses a WireMock.Net server so it behaves like an external API. See the [REST demo](../examples/rest-demo.md) for the runnable example and source links:

- `Setup.cs` starts WireMock and registers named clients and coverage.
- `OrderApiTests.cs` shows attribute-based authentication and shape matching.
- `InventoryApiTests.cs` shows fluent authentication.

Run the complete implementation with:

```bash
dotnet test samples/ProtoTest.Rest.Demo/ProtoTest.Rest.Demo.csproj
```
