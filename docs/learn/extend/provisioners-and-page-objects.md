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
    'The sample cloned. Part 2 also needs Playwright\'s Chromium. Without it the journey skips.',
  ]}
/>

This lesson has two parts: test data first, then a browser screen. Read them in either order.

## The problem

A test that inserts rows directly skips the product's rules, and a copied fixture drifts from its endpoint. A browser test full of CSS selectors says nothing about which screen broke.

A provisioner creates an object through the door you choose and returns what the system gave back. A page object describes a screen, so a test names elements instead of selectors.

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

The sample's API provisioners share one base class, so each names only the URL, the body and the id:

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
    {line: 9, title: 'Name the identity', note: 'A later call finds the same value by this id.'},
    {line: 16, title: 'Use the test\'s clients', note: 'context.Execution is the test that asked for the fixture.'},
    {line: 19, title: 'Require the contract', note: 'A creation without 201 fails the test here.'},
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
| `Create · ProvisionTenantRequest`, 137.0 ms | the request arrived and found its provisioner |
| `Build · ProvisionTenantRequest`, 3.5 ms | the defaults built the value to send |
| `Provision · ProvisionTenantRequest → TenantResponse`, 132.0 ms | the provisioner made the call |
| `Release · data:TenantResponse:1`, 10.3 ms, then `Cleanup · TenantResponse` | teardown released the value and cleaned it up |

The test knew no port or route. The registration chose the implementation, and the trace names it.

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
    {line: 3, title: 'One property, one element', note: 'Found on the live page each time it is read.'},
    {line: 16, title: 'Rows are components', note: 'Each row has its own class with its own elements.'},
    {line: 18, title: 'Matching is strict', note: 'It finds exactly one row, or fails.'},
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

The execution phase shows `web.navigate`, `web.flow`, `web.fill`, `web.click` and `assert.web`. Each entry carries the component path, from `ProjectsPage` down to the element the step touched.

If the journey skips, the reason names the missing browser. The [web integration page](/docs/integrations/web/) covers the install.

## What happened

Both parts follow one idea: put the knowledge in one named place and let the trace show it was used. A provisioner holds how to create a fixture. A page object holds how to find an element, so a failure names the screen instead of a CSS selector.

## Check yourself

<Checkpoint
  question="A provisioner returns a result with an identity and a cleanup. What does the identity let another call do, and what happens to the cleanup when the test ends?"
  verify={<>Read the provisioner reference, then the release rows of the first journey's trace.</>}>

The identity lets a later call find the same value again, through <code>Ref&lt;T&gt;</code>. The cleanup runs at teardown, in reverse creation order, as a <code>data.cleanup</code> operation.

</Checkpoint>

<Checkpoint
  question="A step in the browser journey fails on the projects screen. What three things does the trace name, and what would a raw selector name instead?"
  verify={<>Run the WebJourney filter above and read the <code>web.*</code> entries.</>}>

The screen, the component and the element, as one path from <code>ProjectsPage</code> down. A raw selector names only the CSS.

</Checkpoint>

## Remember

- A provisioner turns a built request into a created value and reports its identity.
- A page object names elements as properties, and the trace carries the component path of what a step touched.

Next: [write an integration](/learn/extend/write-an-integration).

## Go deeper

- [Provisioners](/docs/integrations/data/provisioners): the contract, the identity map, cleanup and trace attributes.
- [Web integration](/docs/integrations/web/): install, probe and the page object model.
