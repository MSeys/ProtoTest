---
sidebar_position: 4
title: Created through the API, shown in the browser
description: Arrange a project through the API with a data builder, log a browser in, and check that the projects page renders it.
---

# Created through the API, shown in the browser

A browser test that clicks through a form to create its own data is slow, and it fails for reasons that have nothing to do with the page under test. Arrange through the API instead, and let the browser do only what the test is about: showing the result.

## Compose

```csharp
using ProtoTest.Core;
using ProtoTest.Data;
using ProtoTest.Rest;
using ProtoTest.Web;

protected override void Configure(IProtoHostBuilder builder) =>
    builder
        .AddApplication("Api", app => app
            .AddRest(rest => rest.AddClient("Api")))
        .AddData(data => data.AddDefaultsFromAssembly(typeof(Setup).Assembly))
        .AddDataProvisioner<CreateProjectRequest, ProjectResponse, ProjectApiProvisioner>()
        .AddWeb(options => options.InstallBrowsers = true);
```

A browser needs an address it can open, and an in-process application has none. Here the application runs on its own — started locally, in a container, or deployed — and `ProtoTest:Applications:Api:BaseUrl` points at it. The REST client and the browser both use that address. To have the run start the application itself, declare it as [infrastructure](../foundation/infrastructure.md).

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
        await row.Cell("Project").ShouldHaveTextAsync(name);
        await row.Cell("Status").ShouldHaveTextAsync(project.Status);
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

## Watch for

- **Assert what the API returned.** `project.Status` comes from the application, so the test checks that the page shows what the system decided, not a value the test made up.
- **Search for your own row.** Other tests create projects in the same application. A name built from `TestId`, and a row matched by it, keeps the test independent of whatever else is listed.
- **The browser is logged in before the body runs.** `[LoginAs]` runs during setup, so a login problem fails as setup, not as a missing row.

When the row is missing, the trace shows the provisioning request and its response next to the browser's steps, and the failed check comes with a screenshot of the page. See [Diagnostics](../integrations/web/diagnostics.md).
