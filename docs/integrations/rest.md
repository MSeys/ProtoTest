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

Named clients use `IHttpClientFactory`, so handlers and connections can be pooled while each test still owns its `HttpClient`. The optional third argument exposes the standard factory builder for headers, handlers and policies:

```csharp
builder.AddRest(rest => rest.AddClient(
	"Orders",
	"https://localhost:5001",
	http => http
		.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(20))
		.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler())));
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
[Auth<BearerTokenAuthenticator>("orders-token")]
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

For an environment created separately for every test, resolve the base address from the test context instead of configuring it globally:

```csharp
builder.AddRest(rest => rest.AddClient(
	"SaaS",
	context => context.Context<EnvironmentContext>().BaseUri));
```

The client itself is still created through `IHttpClientFactory`. Every request target goes through the same URI pipeline: route and query values are applied first; an already absolute target wins; otherwise the per-test resolver or configured `HttpClient.BaseAddress` supplies the base address. A per-test resolver is called only when its base address is actually needed. By then all `ProtoAttribute.BeforeTestAsync` methods have completed, so an environment attribute can safely create an application instance and store its URL in `EnvironmentContext`.

`RestResponse` owns its underlying `HttpResponseMessage`. Dispose the response with `using var` after assertions and any direct `RawResponse` access are complete.

Method-level attributes can be used when one test needs different configuration. A method-level setting takes precedence over the corresponding inherited class-level setting:

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[RestClient("Orders")]
[Auth<BearerTokenAuthenticator>("orders-token")]
public sealed class MixedApiTests
{
	[ProtoTest]
	public Task UsesTheClassDefaults()
	{
		return Task.CompletedTask;
	}

	[ProtoTest]
	[RestClient("Inventory")]
	[Auth<BearerTokenAuthenticator>("inventory-token")]
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
[Auth<BearerTokenAuthenticator>("orders-token")]
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

This class-level pattern is especially useful for a suite targeting one named client with the same default authentication. The demo uses it throughout [`ProtoTest.Demo`](../../samples/ProtoTest.Demo).

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

`Auth<TAuthenticator>` resolves constructor dependencies from the per-test service scope. The authenticator itself is created lazily on the first request, after the ProtoTest attributes have prepared the test. It also receives the active context explicitly:

```csharp
public sealed class UserAuthenticator : IRestAuthenticator
{
	public async ValueTask AuthenticateAsync(
		RestAuthenticationContext context,
		CancellationToken cancellationToken = default)
	{
		var user = context.Test.Context<UserContext>();
		var token = await user.GetAccessTokenAsync(cancellationToken);
		context.Request.Headers.Authorization =
			new AuthenticationHeaderValue("Bearer", token);
	}
}

[RestClient("SaaS")]
[Auth<UserAuthenticator>]
public sealed class AuthorizedApiTests
{
	// An EnvironmentAttribute and UserAttribute can populate the two contexts
	// before the test sends its first request.
}
```

This means an authenticator does not need ambient state and can read a `UserContext`, configuration, or scoped services in a runner-independent way. `BearerTokenAuthenticator`, `ApiKeyAuthenticator`, and `BasicAuthAuthenticator` remain optional building blocks for simple cases, while `[Auth<T>]` is the single attribute mechanism for both built-in and application-specific login flows. Multiple authentication attributes can be combined; their optional `Order` property controls execution order. A method-level authentication set replaces the inherited class-level set.

Class-level authentication can be skipped for a setup or provisioning request:

```csharp
using var response = await context.Rest("SaaS")
	.WithoutAuth()
	.Body(new { role = "billing-admin" })
	.PostAsync("/test-support/users");
```

A typical SaaS lifecycle is therefore: an environment attribute stores `EnvironmentContext`; a later user attribute provisions a user (using `WithoutAuth` where necessary) and stores `UserContext`; then `[Auth<UserAuthenticator>]` applies that user's credentials when the test sends requests. Use `ProtoAttribute.Order` when the environment must exist before user provisioning.

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
		status = JsonValue.Regex("^(confirmed|pending)$"),
		total = 75m
	});
```

`ShouldMatchShape` checks only the properties described by the expected object. `JsonValue` matchers express values that are intentionally not exact, such as a regular expression or a range.

Objects, string-keyed dictionaries and arrays are supported. Pass `JsonSerializerOptions` to `ShouldMatchShape` when the expected CLR type uses a naming policy; `[JsonPropertyName]` is also honored. Arrays use exact-length, position-based matching while objects remain partial shapes.

Assertion failures use runner-independent ProtoTest exceptions. `RestStatusAssertionException` exposes the expected status, actual status, and response body, while `ShapeMismatchException` exposes every structured shape mismatch. Test runners can display their messages directly, and reporting integrations can inspect the structured details without parsing text.

## Diagnostics and sensitive data

Every completed request records an `http.response` observation including duration and the resolved URI. Network, body-reading and cancellation failures record `http.failure`. Diagnostic bodies are limited to 64 KiB by default. Common credential headers, query parameters and JSON properties are redacted before they enter observations, attachments or assertion messages; the raw `RestResponse.Content` remains available to the test.

Customize those policies when enabling attachments:

```csharp
rest.CaptureAttachments(options =>
{
	options.MaxDiagnosticBodyLength = 16 * 1024;
	options.SensitiveHeaders.Add("X-Internal-Secret");
	options.SensitiveJsonProperties.Add("customerSecret");
	options.SensitiveQueryParameters.Add("signature");
});
```

Request builders create fresh content for each send and may be reused sequentially. Besides JSON and text bodies, byte bodies and `Func<HttpContent>` factories are available. Custom HTTP methods can use `SendAsync`; `HeadAsync` and `OptionsAsync` are provided directly.

The convenience API buffers response bodies so JSON, shape assertions and attachments can inspect them repeatedly. To prevent an unexpectedly large or chunked response from exhausting memory, buffering stops at 10 MiB by default. Configure the limit explicitly for file-oriented APIs:

```csharp
rest.ConfigureResponses(options => options.MaxResponseBodyBytes = 50 * 1024 * 1024);
```

Binary content is available through `response.ReadAsBytes()`. Responses exceeding the limit throw `ProtoResponseTooLargeException` and emit an `http.failure` observation.

## Coverage

Each REST request records a route and status hit. `RestCoverageCollector` can be composed through the target builder's common coverage extension:

```csharp
.AddClient("Orders")
.WithCollector<RestCoverageCollector>()
```

When `ProtoTest.OpenApi` is also configured, use `OpenApiCoverageCollector` to map REST hits to endpoints, status codes, and response properties from the contract.

## Runnable example

The [unified control-plane demo](../examples/sample-app.md) is a complete in-process example. Its source includes:

- suite-level ASP.NET Core, REST, GraphQL, coverage, reporting, and tracing configuration in `Setup.cs`;
- attribute-based authentication and response matching across business journeys;
- custom provisioning attributes, hooks, clients, observations, and artifacts.

Run it with:

```bash
dotnet test samples/ProtoTest.Demo/ProtoTest.Demo.csproj
```
