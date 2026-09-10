# Start here: write your first ProtoTest

ProtoTest adds a small amount of setup around an ordinary test method. You configure the suite once, mark tests with `[ProtoTest]`, and use `Proto.Context` for clients, services, state, and integration features.

This guide uses NUnit. The same test model is available through the xUnit, xUnit v3, MSTest, and TUnit adapters.

## 1. Add packages

```bash
dotnet add package ProtoTest.Core
dotnet add package ProtoTest.NUnit
```

For REST tests, also add:

```bash
dotnet add package ProtoTest.Rest
```

## 2. Create suite setup

A suite setup class derives from `ProtoTestAssembly` and configures the host once for the test assembly.

```csharp
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[SetUpFixture]
public sealed class TestSetup : ProtoTestAssembly
{
	protected override void Configure(IProtoHostBuilder builder)
	{
		builder.AddRest(rest => rest
			.AddClient("Orders", "https://localhost:5001"));
	}
}
```

For a less environment-specific setup, supply the base URL through configuration instead:

```csharp
builder.ConfigureAppConfiguration(configuration =>
{
	configuration.AddJsonFile("appsettings.json", optional: false);
});
```

The suite setup is the place for configuration shared by all tests: clients, application configuration, run hooks, test hooks, and collectors. The exact fixture declaration differs slightly between test frameworks; see the [adapter guide](../integrations/nunit.md) for the package list.

## 3. Write a test

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[RestClient("Orders")]
[BearerToken("orders-token")]
public sealed class OrderTests
{
	[ProtoTest]
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

	[ProtoTest]
	public async Task GetAnotherOrder_UsesTheSameClientAndAuthentication()
	{
		using var response = await Proto.Context.Rest()
			.GetAsync("/orders/{id}", new { id = 43 });

		response.ShouldHaveStatus(HttpStatusCode.OK);
	}
}
```

The adapter starts a test-scoped context before each method and completes it afterward. Class-level attributes apply to every test in the class, so shared client and authentication settings do not need to be copied onto every method.

Use method-level attributes when one test needs a different client or authentication. See [clients and attributes](../guides/clients-and-attributes.md).

## 4. Run the test

```bash
dotnet test
```

For a complete external-style REST example using WireMock.Net, see [`samples/ProtoTest.Rest.Demo`](../../samples/ProtoTest.Rest.Demo).

## What can you use in a test?

`Proto.Context` gives the current test access to:

- named clients such as REST `HttpClient` instances;
- services registered in the ProtoTest scope;
- custom per-test state shared with hooks and collectors;
- integration helpers such as `Proto.Context.Rest()` and ASP.NET Core server services;
- recorded coverage hits.

Continue with:

- [Writing tests with Proto.Context](../guides/writing-tests.md)
- [Sharing context and state](../guides/context-and-state.md)
- [Configuring clients and attributes](../guides/clients-and-attributes.md)
- [Extending the lifecycle](../extending/hooks.md)
- [REST clients and authentication](../integrations/rest.md)
