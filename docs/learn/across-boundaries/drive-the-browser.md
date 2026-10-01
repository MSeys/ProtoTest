---
id: drive-the-browser
title: Drive the browser with a page object
sidebar_position: 4
description: "Describe a page once as a class, then sign in and read a project row in a real browser."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

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
    <>The previous lesson, <Link to="/learn/across-boundaries/check-the-database">Check what the application stored</Link></>,
    'The sample checkout from Start, with its .NET prerequisites',
    'Playwright Chromium installed, or automatic browser installation enabled (see step 1)',
  ]}
/>

## The problem

When tests repeat selectors such as `#login-btn`, one markup change can require edits in many tests. A page object keeps those selectors in a class.
This lesson uses the sample's page objects to sign in and check a project row in a real browser.

## Do it

### 1. Check the browser, then run the test alone

The sample uses Playwright's Chromium browser by default. Its test class carries `[RequiresPlaywrightBrowser]`.
With the default settings, a missing browser produces a skip reason instead of an attempted launch.

After the first build, install it with `pwsh samples/Northstar.ProtoTest/bin/Debug/net8.0/playwright.ps1 install chromium`.
Alternatively, set `ProtoTest:Web:Playwright:InstallBrowsers=true` in configuration, or select a channel for an installed system browser.
The availability check does not prove that launching or installing a browser will succeed. Those failures can still fail the test.

Run this command from the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~AProjectCreatedThroughTheApiAppearsOnThePage"
```

A successful run reports one passed test. With the default settings and no Playwright Chromium installation, it reports one skipped test.
If it skips, resolve the reported browser requirement and rerun before looking for browser operations in the trace.

### 2. Read the page objects

Read these excerpts from `samples/Northstar.ProtoTest/Pages.cs`. The sample already contains these classes:

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

`WebPage` and `WebComponent` are ProtoTest's base classes for a page object, and a `WebElement` is one element on the page. Each element property locates an element by its test id. `Rows` describes a collection of project rows, and `Project(name)` selects a row by its text.
A `WebComponent` groups elements within part of a page, such as one project row. The tests use these named properties instead of repeating their selectors.

### 3. Prepare the data through the API

```csharp
var name = $"browser-{Proto.Context.TestId}";
using var created = await Proto.Context.Rest(NorthstarTargets.Api)
    .Body(new CreateProjectRequest(name))
    .PostAsync("/api/v1/projects");
created.Should.HaveHttpStatus(HttpStatusCode.Created);
```

This excerpt comes from `samples/Northstar.ProtoTest/WebJourney.cs`. Its attributes prepare a tenant and identity before the test body runs.
Here, the API creates the project so the browser test can focus on displaying it. A test of the creation form would use the browser instead.

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

`Page<SignInPage>()` gives a page object but does not navigate. `OpenAsync("/login")` does.
The sample's login form exchanges the tenant token for a cookie and redirects to `/projects`.
A flow is a named group of browser steps. In the trace it becomes one operation, with the fill and the click as its children.

`HaveTextAsync` polls for text equal to `ProjectStatuses.Active`. `NorthstarPages.Wait` supplies a 15-second assertion timeout.
If the check times out, its failure includes the last observation. Backend operations have their own timing, so elapsed time can exceed the assertion timeout.
The test needs no fixed sleep.

### 5. Look at the trace

Open the newest `.prototrace` under `samples/Northstar.ProtoTest/bin/Debug/net8.0/TestResults/` in the [viewer](https://trace.prototest.dev). Select `AProjectCreatedThroughTheApiAppearsOnThePage`.
In its execution phase, find `Sign in with the tenant token` and inspect the fill and click beneath it.
If a flow step fails, that operation and the containing flow fail. Later steps in that flow do not run.

## What happened

The test used the API to create data and the browser to read it. In the default local configuration, both application instances share the same store.
The class's `[Application(NorthstarTargets.Web)]` selects the web application. The sample serves it on a loopback listener because the browser needs a network address.
The REST call explicitly selects `NorthstarTargets.Api`, whose default local transport uses an in-process test server.

The page classes separate selectors from the test's actions and assertions. A selector change belongs in `Pages.cs`. A changed user journey may also need test changes.

## Check yourself

<Checkpoint question="Where would you change the test if the Search box changed its test id from search to filter?">

Change `By.TestId("search")` to `By.TestId("filter")` in `ProjectsPage.Search`. The sample's later filtering step uses `page.Search`, so that test code stays the same.

</Checkpoint>

## Remember

- One class per page, one property per element, selectors in one place.
- Arrange through the API when the browser behavior under test does not include creating that data.
- Waits are part of the check (`HaveTextAsync`), not a sleep in the test.
- With default browser settings, `[RequiresPlaywrightBrowser]` skips a missing installation with a reason.

Next: [Follow a message](./follow-a-message.md).

## Go deeper

- [Web integration](/docs/integrations/web): backends, sessions and diagnostics.
- [Pages and components](/docs/integrations/web/page-objects): the model in full.
- [Flows](/docs/integrations/web/flows): named steps in the trace.
