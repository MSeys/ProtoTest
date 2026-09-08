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

The optional factory callback can customize the underlying `WebApplicationFactory<TProgram>`.

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

## Runnable example

See the [ASP.NET Core demo](../examples/aspnetcore-demo.md) for a minimal application and tests covering both HTTP requests and application DI.
