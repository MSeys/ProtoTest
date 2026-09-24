---
sidebar_position: 4
title: Created through the API, shown in the browser
description: Arrange a project through the API with a data builder, log a browser in, and check that the projects page renders it.
---

# Created through the API, shown in the browser

A browser test that clicks through a form to create its own data is slow, and it fails for reasons that have nothing to do with the page under test. Arrange through the API instead, and let the browser do only what the test is about: showing the result.

The same journey runs in the demo — `ApiThenBrowserJourney` in [CrossLayerJourneys.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/CrossLayerJourneys.cs) (test) and [Setup.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs) (host). The full API surface is in [Web](../integrations/web/index.md); arranging is covered by [Data](../integrations/data/index.md).

## Compose

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder
        .AddApplication("Api", app => app
            .AddRest(rest => rest.AddClient("Api")))
        .AddData(data => data.AddDefaultsFromAssembly(typeof(Setup).Assembly))
        .AddDataProvisioner<CreateProjectRequest, ProjectResponse, ProjectApiProvisioner>()
        .AddWeb(options => options.InstallBrowsers = true);
```

A browser needs an address it can open, and an in-process application has none. Here the application runs on its own — started locally, in a container, or deployed — and `ProtoTest:Applications:Api:BaseUrl` points at it. The REST client and the browser both use that address. To have the run start the application itself, declare it as [infrastructure](../foundation/infrastructure.md) and gate the test with `[RequiresCapability(ProtoCapabilityKinds.Server, CapabilityName = "…")]`.

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

- **The browser needs a real address.** An in-process application has none, so this recipe either drives a standalone instance the run starts or a deployed one; `[RequiresCapability]` keeps it honest where neither exists.
- **Assertions poll, with a default timeout.** `Should.HaveTextAsync` waits for the text to appear (5 s by default) rather than reading once; a slow client still fails if it arrives after the timeout.
- **Search for your own row.** Other tests create projects in the same application; a name built from `TestId` keeps the row matching independent of whatever else is listed.
- **The login strategy is application-specific.** `[LoginAs]` runs during setup, so a login problem fails as setup, not as a missing row.
