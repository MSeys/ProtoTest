# ProtoTest.AspNetCore

Hosts an ASP.NET Core application in-process with `WebApplicationFactory<TProgram>`.

```bash
dotnet add package ProtoTest.AspNetCore
```

## Includes

- Registration through `AddAspNetCoreServer<TProgram>()`.
- `ProtoTestHost.For<TProgram>(builder)` composes the application in-process under the default name `Api` in one line.
- `AddLoopbackApplication(applicationName, createApp)` for browser journeys: the hand-built application on its own loopback listener.
- Access to the application's HTTP client, services and service scopes from the test context.
- Per-test service substitution: `context.Override<T>()` in the test body, `[ReplaceService<T>]` and `[FailDependency<T>]` as attributes.
- App-side test-user authentication: `webHost.AddTestUserAuthentication()` authenticates requests as the test's `[SignedInAs]` identity, so the application's own authorization decides.
- Per-run hosting by default, with per-test hosting available when isolation requires it.

Per-run hosting shares application state between tests. Data still needs to be isolated or cleaned up by the suite.

## Learn more

- [ASP.NET Core integration](https://prototest.dev/docs/integrations/aspnetcore)
- [Demo setup](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
