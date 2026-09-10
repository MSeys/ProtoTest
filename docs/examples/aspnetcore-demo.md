# ASP.NET Core demo

The ASP.NET Core demo shows how to host an application in the test process, replace application services, use REST assertions against its in-memory client, exercise success and validation responses, and inspect scoped application services safely.

## Run it

From the repository root:

```bash
dotnet test samples/ProtoTest.AspNetCore.Demo/ProtoTest.AspNetCore.Demo.csproj
```

## Source files

- [`Setup.cs`](https://github.com/matthiasseys/ProtoTest/blob/main/samples/ProtoTest.AspNetCore.Demo/Setup.cs) registers the application server.
- [`OrderApiTests.cs`](https://github.com/matthiasseys/ProtoTest/blob/main/samples/ProtoTest.AspNetCore.Demo/OrderApiTests.cs) tests GET, POST, validation, response shapes, and scoped service isolation.
- [`appsettings.json`](https://github.com/matthiasseys/ProtoTest/blob/main/samples/ProtoTest.AspNetCore.Demo/appsettings.json) contains the demo configuration.

For the concepts behind the example, read the [ASP.NET Core integration guide](../integrations/aspnetcore.md).
