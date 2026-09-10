# ASP.NET Core integration

`ProtoTest.AspNetCore` hosts an ASP.NET Core application in the test process using `WebApplicationFactory<TProgram>`. The test can use an application `HttpClient` and resolve services from the application's container.

## Configure the server

```csharp
using ProtoTest.AspNetCore;

protected override void Configure(IProtoHostBuilder builder)
{
	builder.AddAspNetCoreServer<Program>("OrderApi");
}
```

The optional web-host callback supports the normal `WebApplicationFactory<TProgram>` customization model, including service replacement through `ConfigureTestServices`. A separate client callback configures redirects, cookies, and the generated client's base address.

```csharp
builder.AddAspNetCoreServer<Program>(
	"OrderApi",
	webHost => webHost.ConfigureTestServices(services =>
	{
		services.RemoveAll<IEmailSender>();
		services.AddSingleton<IEmailSender, RecordingEmailSender>();
	}),
	client => client.AllowAutoRedirect = false);
```

## Combine local and external clients

Register REST before ASP.NET Core to use a configured external endpoint when available and fall through to the in-process application otherwise:

```csharp
protected override void Configure(IProtoHostBuilder builder)
{
	builder
		.AddRest(rest => rest.AddClient("OrderApi"))
		.AddAspNetCoreServer<Program>("OrderApi");
}
```

When `ProtoTest:Clients:OrderApi:BaseUrl` has a value, the REST initializer creates the named `HttpClient`. Without a value, it declines and the ASP.NET Core initializer creates the client through `WebApplicationFactory<Program>`. Initializers are tried in registration order, so reversing the two builder calls makes the in-process client take precedence.

## Use the application client

```csharp
using System.Net;
using System.Net.Http.Json;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.NUnit;

[ProtoTest]
public async Task GetPing_ReturnsExpectedPayload()
{
	var client = Proto.Context.Client<HttpClient>("OrderApi");
	var response = await client.GetFromJsonAsync<PingResponse>("/ping");

	Assert.That(response!.Message, Is.EqualTo("pong"));
}
```

## Resolve application services

Use `ServerService<TProgram, TService>` when the test needs to inspect or exercise a registered application service:

```csharp
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.NUnit;

[ProtoTest]
public void ResolveService_ReturnsRegisteredDependency()
{
	var service = Proto.Context.ServerService<Program, ITestMessageService>("OrderApi");

	Assert.That(service.GetMessage(), Is.EqualTo("Hello from AspNetCore DI!"));
}
```

The server factory and its clients are owned by the test context and are disposed during `CompleteTestAsync`.

`ServerService` resolves from the application's root provider and should therefore only be used for singleton services. For scoped services, create and dispose an explicit application scope:

```csharp
using var scope = Proto.Context.CreateServerScope<Program>("OrderApi");
var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
```

## Runnable example

See the [ASP.NET Core demo](../examples/aspnetcore-demo.md) for a runnable application covering service replacement, REST assertions, success and error responses, and scoped application DI.
