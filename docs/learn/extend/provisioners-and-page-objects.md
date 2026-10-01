---
id: provisioners-and-page-objects
title: Provisioners and page objects
sidebar_label: Provisioners and page objects
sidebar_position: 2
description: "Create test data through a provisioner and read its chain in the trace, then model a screen as a page object."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# Provisioners and page objects

<Lesson
  track="Extend ProtoTest"
  step="Lesson 2 of 5"
  minutes={10}
  outcomes={[
    'Write and register a provisioner, then follow its chain through a trace',
    'Model a screen as a page object and find its component paths in a trace',
  ]}
  needs={[
    <>The previous lesson, <a href="/learn/extend/attributes">Write your own attribute</a></>,
    'The sample cloned. The browser journey also needs the Chromium browser that Playwright installs. Without it the journey skips with a reason.',
  ]}
/>

This lesson teaches two separate skills. Part 1 creates test data through the real door. Part 2 describes a browser screen so a test reads like a user. You can read them in either order.

## The problem

Setup data has to come from somewhere. A test that inserts rows directly skips the product's rules. A fixture copied into each test drifts away from the endpoint it once matched. And a browser test full of CSS selectors says nothing about which screen broke.

A provisioner creates an object once, through the door you choose, and returns what the system gave back. A page object describes a screen, so a test names elements instead of selectors.

## Do it

### Part 1: create data with a provisioner

#### 1. Read the shape

A provisioner implements one method. It takes a request and returns the created value and its identity:

```csharp
public interface IProtoDataProvisioner<TInput, TResult>
{
    ValueTask<ProtoDataProvisioningResult<TResult>> CreateAsync(
        TInput value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken);
}
```

The sample gives its API provisioners one shared base class, so a concrete provisioner only names the URL, the body and the id (see `NorthstarMemberProvisioner`):

<AnnotatedCode
  filename="NorthstarApiProvisioner.cs"
  code={`public abstract class NorthstarApiProvisioner<TRequest, TResponse> : IProtoDataProvisioner<TRequest, TResponse>
{
    protected abstract string Url { get; }

    protected abstract object Body(TRequest value);

    protected virtual object? RouteValues(TRequest value) => null;

    protected abstract string IdOf(TResponse response);

    public async ValueTask<ProtoDataProvisioningResult<TResponse>> CreateAsync(
        TRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        using var response = await context.Execution.Rest()
            .Body(Body(value))
            .PostAsync(Url, RouteValues(value), ct: cancellationToken);
        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var created = response.ReadAsJson<TResponse>()
            ?? throw new InvalidOperationException(
                $"The sample app returned no provisioned {typeof(TResponse).Name}.");
        return new ProtoDataProvisioningResult<TResponse>(created, IdOf(created));
    }
}`}
  callouts={[
    {line: 1, title: 'One seam, many fixtures', note: 'The input and the result are separate types, so a request can differ from what the system returns.'},
    {line: 9, title: 'Name the identity', note: 'A later call finds the same value again by this id, through Ref<T>.'},
    {line: 16, title: 'Use the running test context', note: 'context.Execution carries the clients, configuration and trace of the test that asked for the fixture.'},
    {line: 19, title: 'Require the contract', note: 'A creation that did not answer 201 fails the test here. It is a failed fixture, not a finding.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Provisioners/</code>.</>}
/>

#### 2. Register it

One line on the host builder names the request, the result and the implementation:

```csharp
builder.AddDataProvisioner<InviteMemberRequest, MembershipResponse, NorthstarMemberProvisioner>();
```

A defaults module fills values that every fixture of a kind shares, so a test only sets what it cares about:

```csharp
public sealed class NorthstarDataDefaults : IProtoDataDefaultsModule
{
    public void Configure(ProtoDataConfiguration data)
    {
        data.For<ProvisionTenantRequest>()
            .Default(request => request.PlanId, PlanIds.Free);
        data.For<InviteMemberRequest>()
            .Default(
                request => request.Email,
                context => $"member-{context.TestId}-{context.ObjectSequence:D4}@example.test");
    }
}
```

#### 3. Use it and read the chain

A test creates a project with the extension method the sample keeps for it:

```csharp
var project = await Proto.Context.Data().CreateProjectAsync($"provision-{Proto.Context.TestId}");
```

One call writes a chain of entries. This one is from the first journey's trace, <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, where the tenant attribute from the last lesson made the same kind of call:

| Entry | Reading |
| --- | --- |
| `Create · ProvisionTenantRequest`, 137.0 ms | The data surface received the request and found the registered provisioner. |
| `Build · ProvisionTenantRequest`, 3.5 ms | The defaults and the `With` calls produced the value that was sent. |
| `Provision · ProvisionTenantRequest → TenantResponse`, 132.0 ms | The provisioner made the call and returned the created value. |
| `Release · data:TenantResponse:1`, 10.3 ms, then `Cleanup · TenantResponse` | Teardown released the tracked value and ran the cleanup. |

Nothing in the test knew a port or a route. The registration chose the implementation, and the trace names it.

### Part 2: model a screen as a page object

#### 4. Describe the screens

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
}`}
  callouts={[
    {line: 3, title: 'One property, one element', note: 'A property finds its element on the live page each time it is read, so a re-rendered screen leaves no stale handle.'},
    {line: 16, title: 'A collection of components', note: 'Each row is its own component class, so a row can hold the name, the status and the environment count.'},
    {line: 18, title: 'Matching is strict', note: 'No match and more than one match are both errors. A filtered list finds exactly one row or fails.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Pages.cs</code>. The wait the journeys use lives beside it in <code>NorthstarPages.Wait</code>.</>}
/>

#### 5. Write the journey as a user

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

#### 6. Run it and read the trace

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~WebJourney"
```

In the execution phase you find `web.navigate` for the open, `web.flow` for each flow, `web.fill` and `web.click` for the steps, and `assert.web` for the status check. Each entry carries the component path, from `ProjectsPage` down to the element the step touched.

If the journey skips, the reason names Playwright and the browser it could not find. The [web integration page](/docs/integrations/web/) covers the install and the probe.

## What happened

Both parts follow one idea: put the knowledge in one named place and let the trace show it was used. A provisioner holds how to create a fixture, so registration decides which implementation runs. A page object holds how to find an element, so a failure names the screen and the component instead of a CSS selector.

Both are ordinary classes. Nothing about them needs the framework except the one registration or the one base class.

## Check yourself

<Checkpoint
  question="A provisioner returns a result with an identity and a cleanup. What does the identity let another call do, and what happens to the cleanup when the test ends?"
  verify={<>Read the provisioner reference, then the release rows of the first journey's trace.</>}>

The identity is how a later call finds the same value again. The data surface keeps one map per test, and <code>Ref&lt;T&gt;</code> resolves a value by its type and identity. The cleanup, when the provisioner supplies one, is disposed at teardown in reverse creation order and recorded as a <code>data.cleanup</code> operation.

</Checkpoint>

<Checkpoint
  question="A step in the browser journey fails on the projects screen. What three things does the trace name, and what would a raw selector name instead?"
  verify={<>Run the WebJourney filter above and read the <code>web.*</code> entries.</>}>

The screen, the component and the element: each entry carries the component path from <code>ProjectsPage</code> down to the element the step touched. A raw selector would name only the CSS that found it, not the screen it belongs to.

</Checkpoint>

## Remember

- A provisioner turns a built request into a created value and reports its identity.
- Registering a provisioner is one line on the host builder.
- A page object names elements as properties, and the trace carries the component path of what a step touched.

Next: [write an integration](/learn/extend/write-an-integration).

## Go deeper

- [Provisioners](/docs/integrations/data/provisioners): the contract, the identity map, cleanup and trace attributes.
- [Web integration](/docs/integrations/web/): install, probe and the page object model.
