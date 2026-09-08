# Configure clients and attributes

Most tests use two kinds of configuration:

- suite configuration registers the available clients and shared services;
- class or method attributes select how a particular test uses them.

## Register a client once

Register a named client during suite setup:

```csharp
protected override void Configure(IProtoHostBuilder builder)
{
	builder.AddRest(rest => rest
		.AddClient("Orders", "https://localhost:5001"));
}
```

The client is initialized for each test context and is available by name:

```csharp
var client = Proto.Context.Client<HttpClient>("Orders");
```

The [REST guide](../integrations/rest.md), [ASP.NET Core guide](../integrations/aspnetcore.md), and [custom client guide](../extending/custom-attributes-and-clients.md) show integration-specific registration.

## Select a client with attributes

For REST tests, `[RestClient]` selects the named client:

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[ProtoTest]
[RestClient("Orders")]
public async Task GetsAnOrder()
{
	var response = await Proto.Context.Rest()
		.GetAsync("/orders/{id}", new { id = 42 });

	response.ShouldHaveStatus(HttpStatusCode.OK);
}
```

If the attribute is omitted, use the client name explicitly:

```csharp
var response = await Proto.Context.Rest("Orders")
	.GetAsync("/orders/{id}", new { id = 42 });
```

## Put shared attributes on the class

Attributes can target both classes and methods. Put common configuration on the class to avoid repeating it:

```csharp
[RestClient("Orders")]
[BearerToken("orders-token")]
public sealed class OrderTests
{
	[ProtoTest]
	public Task GetsAnOrder() => Task.CompletedTask;

	[ProtoTest]
	public Task CreatesAnOrder() => Task.CompletedTask;
}
```

Both tests inherit the client and authentication settings. This is usually the clearest choice when a class represents one API area or tenant.

## Override settings for one method

A method-level attribute overrides the corresponding class-level attribute:

```csharp
[RestClient("Orders")]
[BearerToken("orders-token")]
public sealed class MixedApiTests
{
	[ProtoTest]
	[RestClient("Inventory")]
	[BearerToken("inventory-token")]
	public Task GetsInventory() => Task.CompletedTask;
}
```

Use method-level configuration for an exception, not as the default location for repeated settings.

## Choose an authentication style

REST authentication can be declared as an attribute:

```csharp
[BearerToken("orders-token")]
public sealed class OrderTests
{
	[ProtoTest]
	public Task UsesTheDefaultToken() => Task.CompletedTask;
}
```

Or selected for a single request:

```csharp
var response = await Proto.Context.Rest("Inventory")
	.Auth<BearerTokenAuthenticator>("inventory-token")
	.GetAsync("/inventory/{sku}", new { sku = "notebook" });
```

Use attributes for a stable class or test default. Use fluent authentication when the credential is specific to one request or is selected dynamically.

## Add your own attribute behavior

For custom behavior that should be declared on a class or method, derive from `ProtoAttribute`. For behavior that should apply to every test automatically, use an `IProtoTestHook` instead. See [custom attributes and clients](../extending/custom-attributes-and-clients.md) and [hooks](../extending/hooks.md).

## Next steps

- Use [context and state](context-and-state.md) to share data around a test.
- Add [custom clients](../extending/custom-attributes-and-clients.md).
- Add [coverage](../extending/coverage.md) to a named target.
