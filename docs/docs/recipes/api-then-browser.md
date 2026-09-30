---
sidebar_position: 4
title: Created through the API, shown in the browser
description: Arrange a project through the API, sign a real browser in, and check that the projects page renders it.
---

import TabbedCode from '@site/src/components/TabbedCode';

# Created through the API, shown in the browser

Arrange over REST, check in the browser, keep one trace. A green run prints `Passed AProjectCreatedThroughTheApiAppearsOnThePage`, and the trace holds the REST create, the sign-in flow and the two page checks in that order. The test is first; the listener it needs follows under [Compose](#compose).

## The situation

A browser test that creates its own data through a form is slow. It can fail for reasons outside the page under test. Arrange through the API instead, and let the browser do only what the test is about: showing the result.

The sample suite hosts the application's own listener in the test process and lets the page journey follow that address. The journey is `AProjectCreatedThroughTheApiAppearsOnThePage` in [WebJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/WebJourney.cs).

## The test

The sample suite arranges through a second application, the in-process API, and the loopback instance for the page. Both read the same store, so the browser shows what the API wrote. The listener that gives the browser its URL is composed under [Compose](#compose).

<TabbedCode
  label="The test and the page objects it reads"
  tabs={[
    {
      id: 'test',
      label: 'Test',
      filename: 'WebJourney.cs',
      code: `[Application(NorthstarTargets.Web)]
[WebSession("Default")]
[RequiresPlaywrightBrowser]
[NorthstarMember(PlanIds.Growth)]
public sealed class WebJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task AProjectCreatedThroughTheApiAppearsOnThePage()
    {
        // Arrange over REST: the browser checks only what the test is about.
        var name = $"browser-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest(NorthstarTargets.Api)
            .Body(new CreateProjectRequest(name))
            .PostAsync("/api/v1/projects");
        created.Should.HaveHttpStatus(HttpStatusCode.Created);

        // Sign in through the browser with the tenant token.
        var signIn = Proto.Context.Web().Page<SignInPage>();
        await signIn.OpenAsync("/login");
        var organization = Proto.Context.Resolve<NorthstarOrganizationContext>();
        await signIn.Flow("Sign in with the tenant token")
            .Fill(page => page.Token, organization.OwnerToken)
            .Click(page => page.Submit)
            .RunAsync();

        // The page shows the project the API created.
        var projects = Proto.Context.Web().Page<ProjectsPage>();
        var row = projects.Project(name);
        await row.Status.Should.HaveTextAsync(ProjectStatuses.Active, NorthstarPages.Wait);

        // The search box filters the rows the application rendered.
        await projects.Flow("Filter the project list")
            .Fill(page => page.Search, name)
            .RunAsync();
        await row.Name.Should.HaveTextAsync(name, NorthstarPages.Wait);
    }
}`,
    },
    {
      id: 'pages',
      label: 'Page objects',
      filename: 'Pages.cs',
      code: `public sealed class ProjectsPage : WebPage
{
    public WebElement Search => Element(By.TestId("search"));

    public WebComponentCollection<ProjectRow> Rows => Components<ProjectRow>(By.TestId("project"));

    public ProjectRow Project(string name) => Rows.Matching(By.HasText(name), $"Project[{name}]");
}

public sealed class ProjectRow : WebComponent
{
    public WebElement Name => Element(By.TestId("project-name"));

    public WebElement Status => Element(By.TestId("project-status"));
}`,
    },
  ]}
/>

`Project(...)` and `Status` are page-object members, so the locators live in one place. All of it is the sample suite's own code: the page objects live in [Pages.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/Northstar.ProtoTest/Pages.cs) and the sign-in screen is the application's real exchange of a tenant token for a session. See [Page objects](../integrations/web/page-objects.md) and [Logging in](../integrations/web/login.md).

## Compose

A browser needs a URL. The in-memory test host has none, so the suite starts its own listener. In the sample suite that is one factory method next to `Program`:

```csharp
// Program.cs of the application under test.
public static async Task Main(string[] args) => await CreateApp(args).RunAsync().ConfigureAwait(false);

/// <summary>Builds the application without running it, so a suite can host it.</summary>
public static WebApplication CreateApp(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);
    // ...the application's services, middleware and endpoints...
    return builder.Build();
}
```

`AddLoopbackApplication` starts that factory on `http://127.0.0.1:0` and publishes the bound address as the application's `BaseUrl`. Pass the factory's arguments to `WebApplication.CreateBuilder`: they carry the run's collected configuration, so the hand-built application reads the same addresses the tests do. A container image or a deployed instance replaces the listener without touching the test: a configured `BaseUrl` wins and the listener stays stopped.

```csharp
// Setup.cs: the listener the browser can open, and the readiness probe that waits for it.
builder.AddLoopbackApplication(NorthstarTargets.Web, NorthstarProgram.CreateApp);
builder.AddHttpReadiness(NorthstarTargets.Web, "/health");

builder.AddApplication(NorthstarTargets.Web, app => app
    .AddRest(rest => rest
        .CaptureAttachments()
        .AddClient(NorthstarTargets.Web))
    .AddWeb(options => options.Headless = true));
```

## What the trace shows

The trace reads as one story:

- the REST create with its `http.request` and `http.response`,
- the session (`web.session.initialize`), the sign-in flow's `web.navigate`, `web.fill` and `web.click`,
- the page's `web.navigate`, the flow and the reads behind the two text assertions (`assert.web`).

Navigations record `web.page.visited`, passing page assertions record `web.page.verified`, and the page inventory records `web.page.available`. Reaching a page is not checking it. Only a verified page counts as covered. See [Web](../integrations/web/index.md#in-the-trace-and-coverage).

A failure in the page checks points at the arrange step or the sign-in flow. All three stay in the same test trace.

## Variations

| Deployment shape | What changes |
| --- | --- |
| The application ships as an image | Start it with `ApplicationContainer` instead of the listener. It maps the port the application listens on and publishes the mapped address as the same `BaseUrl`. See [Infrastructure](../foundation/infrastructure.md). |
| The application already runs elsewhere | Point the application's `BaseUrl` at that instance. A configured address takes precedence over the loopback listener. |
| The API and the page live in one instance | Register one application with `AddRest` and `AddWeb`. The sample suite uses two because only its in-process instance carries the test clock. |
| Selenium instead of Playwright | `AddWeb` selects the backend; its options live under `ProtoTest:Web:Selenium` and `ProtoTest:Web:Playwright`. |

## What it does not prove

- **A published instance is not the test host.** `ServerFactory`, `ApplicationServices` and `[RequiresInProcess]` work only with `AddAspNetCoreServer`. The page inventory and test clock need it too. Reach the loopback or containerized application through its API instead, or register a second application backed by `AddAspNetCoreServer`.
- **Assertions poll, with a bounded wait.** `Should.HaveTextAsync` waits for the text to appear until the timeout instead of reading once; a slow client still fails if it arrives later.
- **Search for your own row.** Other tests create projects in the same application; a name built from `TestId` keeps the row matching independent of whatever else is listed.
- **The sign-in is application-specific.** The sample suite's exchange of a tenant token for a session is the application's own flow, not a framework feature.
