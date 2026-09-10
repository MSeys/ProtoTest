# REST integration

`ProtoTest.Rest` provides named `HttpClient` instances, route templates, authentication, JSON request bodies, response assertions, and REST observations.

## Configure a named client

```csharp
protected override void Configure(IProtoHostBuilder builder)
{
	builder.AddRest(rest => rest
		.AddClient("Orders")
		.WithCollector<RestCoverageCollector>());
}
```

The base URL can be supplied explicitly:

```csharp
builder.AddRest(rest => rest.AddClient("Orders", "https://localhost:5001"));
```

Or through configuration:

```json
{
  "ProtoTest": {
	"Clients": {
	  "Orders": {
		"BaseUrl": "https://localhost:5001"
	  }
	}
  }
}
```

## Make a request

Select the client with `[RestClient]` or with `Proto.Context.Rest("Orders")`. Attributes can be applied to the test class when every test in that class uses the same client or authentication. They are inherited by the test methods, so common setup does not need to be repeated:

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
		using var response = await Proto.Context.Rest()
			.GetAsync("/orders/{id}", new { id = 42 });

		response.ShouldHaveStatus(HttpStatusCode.OK);
	}

	[ProtoTest]
	public async Task CreateOrder_ReturnsCreatedOrder()
	{
		using var response = await Proto.Context.Rest()
			.Body(new { product = "notebook", quantity = 2 })
			.PostAsync("/orders");

		response.ShouldHaveStatus(HttpStatusCode.Created);
	}
}
```

`RestResponse` owns its underlying `HttpResponseMessage`. Dispose the response with `using var` after assertions and any direct `RawResponse` access are complete.

Method-level attributes can be used when one test needs different configuration. A method-level setting takes precedence over the corresponding inherited class-level setting:

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[RestClient("Orders")]
[BearerToken("orders-token")]
public sealed class MixedApiTests
{
	[ProtoTest]
	public Task UsesTheClassDefaults()
	{
		return Task.CompletedTask;
	}

	[ProtoTest]
	[RestClient("Inventory")]
	[BearerToken("inventory-token")]
	public Task OverridesTheDefaultsForThisTest()
	{
		return Task.CompletedTask;
	}
}
```

For a single test, the equivalent method-level form is:

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[ProtoTest]
[RestClient("Orders")]
[BearerToken("orders-token")]
public async Task GetOrder_ReturnsExpectedOrder()
{
	using var response = await Proto.Context.Rest()
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
```

This class-level pattern is especially useful for a suite of tests targeting one named client with the same default authentication. The demo uses this pattern in [`OrderApiTests.cs`](../../samples/ProtoTest.Rest.Demo/OrderApiTests.cs).

The same authentication can be selected fluently for one request:

```csharp
using var response = await Proto.Context.Rest("Inventory")
	.Auth<BearerTokenAuthenticator>("inventory-token")
	.GetAsync("/inventory/{sku}", new { sku = "notebook" });
```

You can also pass an authenticator instance:

```csharp
using var response = await Proto.Context.Rest("Inventory")
	.Auth(new BearerTokenAuthenticator("inventory-token"))
	.GetAsync("/inventory/{sku}", new { sku = "notebook" });
```

## Request bodies and assertions

```csharp
using var response = await Proto.Context.Rest("Orders")
	.Body(new { product = "notebook", quantity = 2 })
	.PostAsync("/orders");

response
	.ShouldHaveStatus(HttpStatusCode.Created)
	.ShouldMatchShape(new
	{
		id = 43,
		status = IsRest.Regex("^(confirmed|pending)$"),
		total = 75m
	});
```

`ShouldMatchShape` checks only the properties described by the expected object. `IsRest` matchers express values that are intentionally not exact, such as a regular expression or a range.

Assertion failures use runner-independent ProtoTest exceptions. `RestStatusAssertionException` exposes the expected status, actual status, and response body, while `ShapeMismatchException` exposes every structured shape mismatch. Test runners can display their messages directly, and reporting integrations can inspect the structured details without parsing text.

## Coverage

Each REST request records a route and status hit. `RestCoverageCollector` can be composed through the target builder's common coverage extension:

```csharp
.AddClient("Orders")
.WithCollector<RestCoverageCollector>()
```

When `ProtoTest.OpenApi` is also configured, use `OpenApiCoverageCollector` to map REST hits to endpoints, status codes, and response properties from the contract.

## Runnable example

The [REST demo](../examples/rest-demo.md) is a complete WireMock-backed example. Its source includes:

- suite-level external API configuration in `Setup.cs`;
- attribute-based authentication and response matching in `OrderApiTests.cs`;
- fluent authentication in `InventoryApiTests.cs`.

Run it with:

```bash
dotnet test samples/ProtoTest.Rest.Demo/ProtoTest.Rest.Demo.csproj
```
