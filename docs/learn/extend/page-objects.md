---
id: page-objects
title: Model a screen as a page object
sidebar_label: Model a screen
sidebar_position: 3
description: "Describe a screen as a page object, write a browser journey against it, and find its component paths in the trace."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# Model a screen as a page object

<Lesson
  track="Extend ProtoTest"
  step="Lesson 3 of 6"
  minutes={8}
  outcomes={[
    'Model a screen as a page object',
    'Find the component path of each browser step in a trace',
  ]}
  needs={[
    <>The previous lesson, <a href="/learn/extend/provisioners">Create test data with a provisioner</a></>,
    'The sample cloned and Playwright\'s Chromium installed. Without it the journey skips.',
  ]}
/>

## The problem

A browser test full of CSS selectors says nothing about which screen broke. When the markup changes, every test that copied the selector changes with it.

A page object describes a screen, so a test names elements instead of selectors.

## Do it

### 1. Describe the screens

`Pages.cs` describes the two screens the browser journey uses. Each property names one element:

<AnnotatedCode
  filename="Pages.cs"
  code={`public sealed class SignInPage : WebPage
{
    public WebElement Token => Element(By.TestId("token"));

    public WebElement Submit => Element(By.TestId("login"));

    public WebElement Error => Element(By.TestId("error"));
}

public sealed class ProjectsPage : WebPage
{
    public WebElement Table => Element(By.TestId("projects"));

    public WebElement Search => Element(By.TestId("search"));

    public WebComponentCollection<ProjectRow> Rows => Components<ProjectRow>(By.TestId("project"));

    public ProjectRow Project(string name) => Rows.Matching(By.HasText(name), $"Project[{name}]");
}

public sealed class ProjectRow : WebComponent
{
    public WebElement Name => Element(By.TestId("project-name"));

    public WebElement Status => Element(By.TestId("project-status"));
}`}
  callouts={[
    {line: 3, title: 'One property, one element', note: 'Found on the live page each time it is read.'},
    {line: 16, title: 'Rows are components', note: 'Each row has its own class with its own elements.'},
    {line: 18, title: 'Matching is strict', note: 'It finds exactly one row, or fails.'},
    {line: 25, title: 'Found inside its row', note: 'Status is looked up within the matched row, not the whole page.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Pages.cs</code>. The wait the journeys use lives beside it in <code>NorthstarPages.Wait</code>.</>}
/>

### 2. Write the journey as a user

```csharp
var signIn = Proto.Context.Web().Page<SignInPage>();
await signIn.OpenAsync("/login");
await signIn.Flow("Sign in with the tenant token")
    .Fill(page => page.Token, organization.OwnerToken)
    .Click(page => page.Submit)
    .RunAsync();

var projects = Proto.Context.Web().Page<ProjectsPage>();
var row = projects.Project(name);
await row.Status.Should.HaveTextAsync(ProjectStatuses.Active, NorthstarPages.Wait);
```

### 3. Run it and read the trace

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~WebJourney"
```

The execution phase shows `web.navigate`, `web.flow`, `web.fill`, `web.click` and `assert.web`. Each entry carries the component path, from `ProjectsPage` down to the element the step touched.

If the journey skips, the reason names the missing browser. The [web integration page](/docs/integrations/web/) covers the install.

### 4. Break one locator and read the failure

In `Pages.cs`, change the row's status test id from `project-status` to `project-state`, then run the journey again. It fails after the 15-second wait:

```text
Element 'ProjectsPage.Project[browser-192244000001].Status' should have text "active" within 00:00:15.
Last observed: Element 'ProjectsPage.Project[browser-192244000001].Status' was not present within 00:00:05.
Locator: TestId("project-state").
```

The message reads like the test: the projects page, the row for this test's project, its status. The locator comes last, as the detail to fix. Each probe waits up to the 5-second action timeout for the element, so the last observation names that shorter wait.

Change the test id back before you go on.

## What happened

The page object holds how to find an element, in one named place. When the markup changes, one property changes, and the failure already said which one: the screen, the component and the element, with the locator as the last detail.

## Check yourself

<Checkpoint
  question="A step in the browser journey fails on the projects screen. What three things does the trace name, and what would a raw selector name instead?"
  verify={<>Run the WebJourney filter above and read the <code>web.*</code> entries.</>}>

The screen, the component and the element, as one path from <code>ProjectsPage</code> down. A raw selector names only the CSS.

</Checkpoint>

## Remember

- A page object names elements as properties. Each read finds the element on the live page.
- The trace carries the component path of what each step touched.

Next: [write an integration](/learn/extend/write-an-integration).

## Go deeper

- [Web integration](/docs/integrations/web/): install, probe and the page object model.
