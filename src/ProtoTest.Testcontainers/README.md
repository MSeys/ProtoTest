# ProtoTest.Testcontainers

Shared container-resource support used by ProtoTest's Testcontainers packages, and the application container for an application under test.

Use it directly to start an application image with the run, or when building another run-scoped container integration.

`ProtoContainerResource<TContainer>` handles start-once and release-once behaviour. Register a resource with `AddInfrastructure(...)` when the host should start it and expose values such as a connection string.

`ApplicationContainer` starts an application image as run infrastructure and publishes the mapped address as `ProtoTest:Applications:{application}:BaseUrl`, so the application's REST clients, browser sessions and the readiness probe resolve that one container:

```csharp
var api = ApplicationContainer.Container("Api", "mcr.microsoft.com/dotnet/samples:aspnetapp", port: 8080);
builder
    .AddInfrastructure(api, api.BaseUrlKey)          // a configured key skips the container
    .AddHttpReadiness(api.Application, "/")
    .AddApplication(api.Application, app => app
        .AddRest(rest => rest.AddClient(api.Application))
        .AddWeb());
```

The published address is `http://{hostname}:{mapped port}`; the default readiness check waits for the declared port to accept a connection, and the `configure` callback adds container build options or a Testcontainers wait strategy. A machine without a container runtime reports the reason through `TryStart`, so the suite can skip instead of failing.

The package does not depend on a specific Testcontainers module. Technology packages supply their own module container (for example `PostgresDatabase`); `ApplicationContainer` builds on the Testcontainers core library.

## Limits

- **The image and its readiness belong to the suite.** `ApplicationContainer` knows only the image and the port it listens on - not the application's health endpoint, environment or protocol. Add `AddHttpReadiness(application, path)` for an HTTP-level wait.
- **One scheme: `http`.** The mapped address is published as `http://…`; an image that only serves HTTPS is not covered - point the application at that environment through `ProtoTest:Applications:{application}:BaseUrl` instead.

## Learn more

- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- [ASP.NET Core](https://prototest.dev/docs/integrations/aspnetcore)
- [Extending ProtoTest](https://prototest.dev/docs/advanced/extending)
- [Package source](https://github.com/MSeys/ProtoTest/tree/main/src/ProtoTest.Testcontainers)
