# ProtoTest.AspNetCore

Hosts an ASP.NET Core application in-process with `WebApplicationFactory<TProgram>`.

```bash
dotnet add package ProtoTest.AspNetCore
```

## Includes

- Registration through `AddAspNetCoreServer<TProgram>()`.
- Access to the application's HTTP client, services and service scopes from the test context.
- Per-run hosting by default, with per-test hosting available when isolation requires it.

Per-run hosting shares application state between tests. Data still needs to be isolated or cleaned up by the suite.

## Learn more

- [ASP.NET Core integration](https://prototest.dev/docs/integrations/aspnetcore)
- [Demo setup](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
