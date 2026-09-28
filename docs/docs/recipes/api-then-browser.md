---
sidebar_position: 4
title: Created through the API, shown in the browser
description: Arrange a project through the API with a data builder, log a browser in, and check that the projects page renders it.
---

# Created through the API, shown in the browser

A browser test that clicks through a form to create its own data is slow, and it fails for reasons that have nothing to do with the page under test. Arrange through the API instead, and let the browser do only what the test is about: showing the result.

The same journey runs in the demo — `ApiThenBrowserJourney` in [CrossLayerJourneys.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/CrossLayerJourneys.cs) (test) and [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs) (host). The full API surface is in [Web](../integrations/web/index.md); arranging is covered by [Data](../integrations/data/index.md).

## Compose

A browser can only open a real address, and `AddAspNetCoreServer`'s in-memory test host has none. The smallest way to run this journey is to host the application's own listener in the test process on port 0 and publish the address it bound as the application's `BaseUrl`: the REST client, the browser session and the readiness probe then all resolve that one running application, and the run releases the listener with the run. When the application already ships as an image, `ApplicationContainer` from `ProtoTest.Testcontainers` does the same with the application's own container — see [Compose with the application's container](#compose-with-the-applications-container).

Give the application a way to be built without being run — one method next to `Program`:

```csharp
// Program.cs of the application under test.
App.Create(args).Run();

public partial class Program;

public static class App
{
    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        // ...the application's services, middleware and endpoints...
        return builder.Build();
    }
}
```

Declare the listener with `AddLoopbackApplication(applicationName, createApp)` from `ProtoTest.AspNetCore`: it starts the factory on `http://127.0.0.1:0`, publishes the address the listener bound as the application's `BaseUrl`, forwards the suite's configuration with the values the infrastructure started before it published as command-line arguments, and releases the application with the run. Registering it with the key it fills keeps the suite portable: a run that configures `ProtoTest:Applications:Api:BaseUrl` skips the listener and points at that environment instead. The composition and its browser proof live in [Setup.cs](../../../tests/ProtoTest.AspNetCore.Web.Tests/Setup.cs); the piece itself in [ProtoHostBuilderExtensions.cs](../../../src/ProtoTest.AspNetCore/ProtoHostBuilderExtensions.cs).

Pass the factory's arguments to `WebApplication.CreateBuilder`: they carry the run's collected configuration, so the hand-built application reads the same addresses the tests do. Arguments the factory ignores never reach the application.

The composition then stays the recipe's shape, with the listener added:

```csharp
protected override void Configure(IProtoHostBuilder builder)
{
    builder
        .AddLoopbackApplication("Api", App.Create)
        .AddApplication("Api", app => app
            .AddRest(rest => rest.AddClient("Api")))
        .AddData(data => data.AddDefaultsFromAssembly(typeof(Setup).Assembly))
        .AddDataProvisioner<CreateProjectRequest, ProjectResponse, ProjectApiProvisioner>()
        .AddWeb(options => options.InstallBrowsers = true);
}
```

When the application already runs elsewhere — started by hand, in its own container, or deployed — publish the same `ProtoTest:Applications:Api:BaseUrl` there and gate the test with `[RequiresCapability(ProtoCapabilityKinds.Server, CapabilityName = "…")]`; the configured key skips the loopback listener. The demo publishes its standalone console exactly that way.

### Compose with the application's container

When the application already ships as an image, start that image as run infrastructure with `ApplicationContainer` (`ProtoTest.Testcontainers`) instead of the listener; it maps the port the application listens on and publishes the mapped address as the same `BaseUrl`:

```csharp
using ProtoTest.Testcontainers;

var api = ApplicationContainer.Container("Api", "my-registry.example.test/orders-api:1.4", port: 8080);
builder
    .AddInfrastructure(
        "OrdersApi",
        chain => chain
            .UseConfigured()    // a configured key skips the container
            .UseContainer(api),
        api.BaseUrlKey)
    .AddHttpReadiness(api.Application, "/health")    // register the probe after the piece that publishes the address
    .AddApplication("Api", app => app
        .AddRest(rest => rest.AddClient("Api"))
        .AddWeb());
```

`port` is the port the application listens on **inside** the image; the published address is `http://{hostname}:{mapped port}`. The default readiness check waits for that port to accept a connection, and the `configure` callback adds container build options (an environment, a command, a Testcontainers wait strategy). The image, its health path and its environment belong to the suite: the piece assumes nothing about the application. `TryStart` reports why a container could not start, so a machine without a container runtime can skip before registering it - the way [tests/ProtoTest.AspNetCore.Web.Tests](../../../tests/ProtoTest.AspNetCore.Web.Tests/Setup.cs) registers the sample image.

Do not back the same application with both the listener and `AddAspNetCoreServer`: the browser and the HTTP clients follow the published address, while `ServerFactory` reaches the separate in-memory instance. Use the listener for a journey that needs one shared application; a suite that also needs the test host gives the published instance its own application name, as the demo does for its standalone console.

`ProjectApiProvisioner` creates a project through `POST /api/projects`; see [Provisioners](../integrations/data/provisioners.md). `ProjectLogin` below is a login strategy for the application's own sign-in; see [Logging in](../integrations/web/login.md).

## The test

```csharp
[Application("Api")]
[LoginAs<ProjectLogin>("owner")]
public sealed class ProjectPageTests
{
    [ProtoTest]
    public async Task A_project_created_through_the_API_is_listed()
    {
        var name = $"atlas-{Proto.Context.TestId}";
        var project = await Proto.Context.Data()
            .For<CreateProjectRequest>()
            .With(request => request.Name, name)
            .CreateAsync<ProjectResponse>();

        var page = Proto.Context.Web().Page<ProjectsPage>();
        await page.OpenAsync("/projects");
        await page.Search.FillAsync(name);

        var row = page.Projects.RowMatching(By.HasText(name));
        await row.Cell("Project").Should.HaveTextAsync(name);
        await row.Cell("Status").Should.HaveTextAsync(project.Status);
    }

    public sealed class ProjectsPage : WebPage
    {
        public WebElement Search => Element(By.TestId("search"));

        public ProjectTable Projects => Component<ProjectTable>(By.TestId("projects"));
    }

    public sealed class ProjectTable : WebTable<ProjectRow>;

    public sealed class ProjectRow : WebTableRow;
}
```

## What it proves

`project.Status` comes from the application, so the test checks that the page shows what the system decided, not a value the test made up. The page object keeps locators out of the assertions, and the whole journey — provisioning request, API response, browser navigation and the two row checks — reads as one story in the trace.

## Limits

- **The browser needs a real address.** `AddAspNetCoreServer`'s in-memory test host has none, so this recipe hosts the application's listener itself, starts the application's image with `ApplicationContainer`, or points the same key at a container or a deployed instance; `[RequiresCapability]` keeps it honest where no address can exist.
- **The published instance is a real instance, not the test host.** `ServerFactory`, `ApplicationServices` and `[RequiresInProcess]` belong to `AddAspNetCoreServer`; the ASP.NET Core page inventory and the run's clock bridge run only with it. Reach the loopback or containerized application through its API instead, or register a second application backed by `AddAspNetCoreServer`.
- **Assertions poll, with a default timeout.** `Should.HaveTextAsync` waits for the text to appear (5 s by default) rather than reading once; a slow client still fails if it arrives after the timeout.
- **Search for your own row.** Other tests create projects in the same application; a name built from `TestId` keeps the row matching independent of whatever else is listed.
- **The login strategy is application-specific.** `[LoginAs]` runs during setup, so a login problem fails as setup, not as a missing row.
