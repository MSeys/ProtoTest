# ASP.NET Core demo

The ASP.NET Core demo shows how to host an application in the test process and use both its HTTP client and application dependency-injection container.

## Run it

From the repository root:

```bash
dotnet test samples/ProtoTest.AspNetCore.Demo/ProtoTest.AspNetCore.Demo.csproj
```

## Source files

- [`Setup.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.AspNetCore.Demo/Setup.cs) registers the application server.
- [`OrderApiTests.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.AspNetCore.Demo/OrderApiTests.cs) tests HTTP endpoints and resolves an application service.
- [`appsettings.json`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.AspNetCore.Demo/appsettings.json) contains the demo configuration.

For the concepts behind the example, read the [ASP.NET Core integration guide](../integrations/aspnetcore.md).
