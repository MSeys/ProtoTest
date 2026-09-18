# ProtoTest.AspNetCore

Hosts an ASP.NET Core application in-process through `WebApplicationFactory<TProgram>` and registers its `HttpClient` as a named, test-scoped ProtoTest client.

```bash
dotnet add package ProtoTest.AspNetCore --prerelease
```

```csharp
builder.AddAspNetCoreServer<Program>("OrderApi");

[ProtoTest]
public async Task Ping()
{
    var client = Proto.Context.Client<HttpClient>("OrderApi");
    using var response = await client.GetAsync("/ping");
    response.EnsureSuccessStatusCode();
}
```

The underlying application factory and application service scopes are also available from the active execution context. See the [ASP.NET Core guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/integrations/aspnetcore.md).
