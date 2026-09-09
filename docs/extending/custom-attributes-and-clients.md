# Add custom attributes and clients

Use custom attributes and client initializers when you want a reusable extension that composes with the existing ProtoTest lifecycle. These are focused extension snippets; combine them with the suite setup from the [first-test guide](../getting-started/first-test.md).

## Custom attributes

A `ProtoAttribute` can be placed on a test class or method. It runs before and after the decorated tests and can use the active `ProtoExecutionContext`.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method,
	AllowMultiple = true,
	Inherited = true)]
public sealed class TenantAttribute(string tenantId) : ProtoAttribute
{
	public override Task BeforeTestAsync(ProtoExecutionContext context)
	{
		context.SetContext(new TenantState(tenantId));
		return Task.CompletedTask;
	}

	public override Task AfterTestAsync(ProtoExecutionContext context)
	{
		var tenant = context.TryContext<TenantState>();
		// Clear external tenant data or record diagnostics.
		return Task.CompletedTask;
	}
}

public sealed record TenantState(string Id) : IProtoContext;
```

Apply it at the class level to avoid repetition:

```csharp
[Tenant("contoso")]
public sealed class TenantTests
{
	[ProtoTest]
	public Task UsesTheTenant() => Task.CompletedTask;
}
```

Use `Order` when a custom attribute must run before or after another attribute or hook:

```csharp
[Tenant("contoso", Order = 100)]
```

Attributes are best for behavior that is meaningful at the test declaration. Use a registered hook when the behavior should apply to the entire suite without decorating classes.

## Custom clients

An `IProtoClientInitializer` creates a client for each test context and registers it under a name. This is the extension point for custom clients such as SDK clients, database connections, message clients, or other disposable resources.

```csharp
public sealed class OrdersClientInitializer(IConfiguration configuration)
	: IProtoClientInitializer<OrdersClient>
{
	public string Name => "OrdersSdk";

	public Task<bool> TryInitializeAsync(
		ProtoExecutionContext context,
		CancellationToken cancellationToken = default)
	{
		var baseUrl = configuration["Orders:BaseUrl"];
		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			return Task.FromResult(false);
		}

		var client = new OrdersClient(new Uri(baseUrl));
		context.RegisterClient(client, Name);
		return Task.FromResult(true);
	}
}
```

Register the initializer through dependency injection:

```csharp
builder.ConfigureServices(services =>
{
	services.AddSingleton<IProtoClientInitializer, OrdersClientInitializer>();
});
```

Use the client in a test:

```csharp
[ProtoTest]
public async Task GetsAnOrderWithTheSdk()
{
	var client = Proto.Context.Client<OrdersClient>("OrdersSdk");
	var order = await client.GetAsync(42);

	Assert.That(order.Id, Is.EqualTo(42));
}
```

The current test context owns registered clients. Disposable clients are disposed in reverse registration order when the test completes. Do not register the same client type and name twice.

Multiple initializers may target the same client type and name. ProtoTest tries them in dependency-injection registration order and stops after the first initializer that returns `true`. An initializer must return `false` without changing the execution context when it cannot provide its client. If every initializer for a client returns `false`, test setup fails.

## Initializer versus hook

Use an initializer when the result is a named client the test or another integration will retrieve. Use a test hook when the behavior is setup/teardown or state preparation. An initializer can be invoked by the standard client-initializer lifecycle hook; it should not be manually called from each test.

## Configuration and services

Initializers can receive services through constructor injection. Put configuration in the host builder rather than reading process-global state directly:

```csharp
builder
	.ConfigureAppConfiguration(configuration =>
	{
		configuration.AddEnvironmentVariables();
	})
	.ConfigureServices(services =>
	{
		services.AddSingleton<OrdersClientInitializer>();
		services.AddSingleton<IProtoClientInitializer>(sp =>
			sp.GetRequiredService<OrdersClientInitializer>());
	});
```

## Next steps

- Use [hooks](hooks.md) for suite and per-test behavior.
- Add custom [coverage collectors](coverage.md).
- See [context and state](../guides/context-and-state.md) for sharing data with the test.
