---
id: drive-the-browser
title: Drive the browser with a page object
sidebar_position: 4
description: "Describe a page once as a class, then sign in and read a project row in a real browser."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Drive the browser with a page object

<Lesson
  track="Across boundaries"
  step="Lesson 4 of 6"
  minutes={9}
  outcomes={[
    'Describe a page as a class of elements',
    'Sign in and read a row in a real browser',
    'Know what happens when the browser is not installed',
  ]}
  needs={[
    <>The previous lesson, <a href="./check-the-database">Check what the application stored</a></>,
    'A Playwright browser installed, or a run that installs it (see step 1)',
  ]}
/>

## The problem

Browser tests break when a page changes a little. If every test repeats selectors such as `#login-btn`, one renamed id breaks fifty tests. The fix is to describe each page once, in one class, and let tests talk about what a user sees.

## Do it

### 1. Check the browser, then run the test alone

The test needs a browser that Playwright can drive. The class carries `[RequiresPlaywrightBrowser]`. If the browser is missing, the test skips with a reason that names Playwright and the ways to fix it: run `playwright.ps1 install <browser>`, set `ProtoTest:Web:Playwright:InstallBrowsers=true`, or configure a channel for an installed system browser. The test does not fail.

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~AProjectCreatedThroughTheApiAppearsOnThePage"
```

With a browser available, it reports one passed test. Without one, it reports one skipped test.

### 2. Read the page objects

`Pages.cs` describes the screens, one class each:

```csharp
public sealed class SignInPage : WebPage
{
    public WebElement Token => Element(By.TestId("token"));

    public WebElement Submit => Element(By.TestId("login"));
}

public sealed class ProjectsPage : WebPage
{
    public WebElement Search => Element(By.TestId("search"));

    public WebComponentCollection<ProjectRow> Rows => Components<ProjectRow>(By.TestId("project"));

    public ProjectRow Project(string name) => Rows.Matching(By.HasText(name), $"Project[{name}]");
}

public sealed class ProjectRow : WebComponent
{
    public WebElement Name => Element(By.TestId("project-name"));

    public WebElement Status => Element(By.TestId("project-status"));
}
```

The classes show only part of the file. Each property names one element by its test id. A `WebComponent` is a repeatable part of a page, here one project row. The selectors live in this file only.

### 3. Prepare the data through the API

```csharp
using var created = await Proto.Context.Rest(NorthstarTargets.Api)
    .Body(new CreateProjectRequest(name))
    .PostAsync("/api/v1/projects");
created.Should.HaveHttpStatus(HttpStatusCode.Created);
```

The browser should not click through a form to create test data. The API does it in one call, and the page then has something to show.

### 4. Sign in and read the row

```csharp
var signIn = Proto.Context.Web().Page<SignInPage>();
await signIn.OpenAsync("/login");
var organization = Proto.Context.Resolve<NorthstarOrganizationContext>();
await signIn.Flow("Sign in with the tenant token")
    .Fill(page => page.Token, organization.OwnerToken)
    .Click(page => page.Submit)
    .RunAsync();

var projects = Proto.Context.Web().Page<ProjectsPage>();
var row = projects.Project(name);
await row.Status.Should.HaveTextAsync(ProjectStatuses.Active, NorthstarPages.Wait);
```

`Page<SignInPage>()` gives a page object but does not navigate. `OpenAsync("/login")` does. A flow groups steps under one name, so the trace shows one operation with the steps nested beneath it.

`HaveTextAsync` waits. It retries until the element shows the text or `NorthstarPages.Wait`, 15 seconds, runs out. The test never sleeps.

### 5. Look at the trace

In the [viewer](https://trace.prototest.dev), find the flow `Sign in with the tenant token` in the execution phase. If a step fails, its steps tell you which one.

## What happened

The test worked on two layers of the same application. The REST call and the browser reach one store, so the project created by the API appears on the page. The `[Application(NorthstarTargets.Web)]` class selects the web instance, which the sample serves on a loopback address, because a browser needs a real listener. The REST call names `NorthstarTargets.Api` explicitly.

The page classes make the test read as what a user does. When the markup changes, you edit `Pages.cs`.

## Check yourself

<Checkpoint question="Where would you change the test if the Search box changed its test id from search to filter?">

Only in `ProjectsPage`, in the `Search` property. The test calls `page.Search` and never mentions the id.

</Checkpoint>

## Remember

- One class per page, one property per element, selectors in one place.
- Create data through the API and use the browser for what only a browser can show.
- Waits are part of the check (`HaveTextAsync`), not a sleep in the test.
- `[RequiresPlaywrightBrowser]` turns a missing browser into a skip with a reason.

Next: [Follow a message](./follow-a-message.md).

## Go deeper

- [Web integration](/docs/integrations/web): backends, sessions and diagnostics.
- [Pages and components](/docs/integrations/web/page-objects): the model in full.
- [Flows](/docs/integrations/web/flows): named steps in the trace.
