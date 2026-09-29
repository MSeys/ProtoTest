---
id: provisioners-and-page-objects
title: Provisioners and page objects
sidebar_label: Provisioners and page objects
sidebar_position: 2
description: "Create fixtures through a provisioner, read the provisioning chain and its cleanup, and model a screen as a page object."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Provisioners and page objects

Two problems come back in every journey: creating a fixture through the real door, and reading a browser screen as something other than markup. The sample answers both in ordinary classes you can copy.

<LearnShell
  level="Level 6, lesson 2"
  minutes="About 9 minutes"
  outcome={[
    'Read a provisioner: the request in, the created value out, the identity and the cleanup.',
    'Register one and follow its chain through a trace.',
    'Model a screen as a page object and find its component paths in a trace.',
  ]}
  before={[
    <>Write your own attribute (<Link to="/learn/make-it-yours/attributes">lesson 1</Link>).</>,
    'The sample cloned. The browser journey also needs Playwright\'s Chromium; it skips with a reason without it.',
  ]}
  situation={
    <>
      <p>Setup data has to come from somewhere. A test that inserts rows directly bypasses every rule the product enforces, and a fixture copy pasted into each test drifts from the route it once matched.</p>
      <p>A provisioner creates the object once, through the door you choose, and returns what the system gave back. A page object describes a screen so a test reads as a user instead of a selector.</p>
    </>
  }
  checkpoint={{
    question:
      'A provisioner returns a result with an identity and a cleanup. What does the identity let another call do, and what happens to the cleanup when the test ends?',
    verify: (
      <>
        Read the provisioner reference, then the release rows of the first journey's trace.
      </>
    ),
    reveal: (
      <>
        The identity is how a later call finds the same value again: the data surface keeps one map per test and <code>Ref&lt;T&gt;</code> resolves a value by its type and identity. The cleanup, when the provisioner supplies one, is disposed at teardown in reverse creation order and recorded as a <code>data.cleanup</code> operation.
      </>
    ),
  }}
  learned={[
    'A provisioner turns a built request into a created value and reports its identity.',
    'Registering it is one line on the host builder, and the same call works through the API or the domain.',
    'A page object names elements as properties, and the trace carries the component path of what was touched.',
  ]}
  next={[
    {
      label: 'Write an integration',
      to: '/learn/make-it-yours/write-an-integration',
      note: 'The extension points behind provisioners, hooks and custom clients.',
    },
    {
      label: 'Provisioners',
      to: '/docs/integrations/data/provisioners',
      note: 'The contract, the identity map, cleanup and the trace attributes.',
    },
  ]}>

## The provisioner contract

Every provisioner implements one method:

```csharp
public interface IProtoDataProvisioner<TInput, TResult>
{
    ValueTask<ProtoDataProvisioningResult<TResult>> CreateAsync(
        TInput value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken);
}
```

The sample gives its API provisioners one shared shape so they cannot drift:

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
    {line: 1, title: 'One seam, many fixtures', note: 'The interface takes an input and a result type, so a request can differ from what the system returns.'},
    {line: 9, title: 'Name the identity', note: 'The id the provisioner reports is how Ref<T> finds the value later, and what the release row names.'},
    {line: 16, title: 'Use the running test context', note: 'context.Execution carries the clients, the configuration and the trace of the test that asked for the fixture.'},
    {line: 19, title: 'Require the contract', note: 'A creation that did not answer 201 is a failed fixture, not a warning. The provisioner fails the test here.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Provisioners/</code>. A concrete provisioner names only the URL, the body and the id, like <code>NorthstarMemberProvisioner</code>.</>}
/>

Registration names the pair and the implementation:

```csharp
builder.AddDataProvisioner<InviteMemberRequest, MembershipResponse, NorthstarMemberProvisioner>();
```

The sample registers its set once, and a defaults module fills values every fixture shares:

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

## Use one, then read the chain

A test provisions a project with the extension the sample keeps for it:

```csharp
var project = await Proto.Context.Data().CreateProjectAsync($"provision-{Proto.Context.TestId}");
```

One call, and the trace records a chain. From the first journey's archive, <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, where the tenant attribute made the same call:

| Entry | Reading |
| --- | --- |
| `Create · ProvisionTenantRequest`, 138.1 ms | the data surface received the request and looked up the registered provisioner |
| `Build · ProvisionTenantRequest`, 3.6 ms | the defaults and the `With` calls produced the value that was sent |
| `Provision · ProvisionTenantRequest → TenantResponse`, 132.9 ms | the provisioner made the call and returned the created value |
| `Release · data:TenantResponse:1`, 10.8 ms, then `Cleanup · TenantResponse` | teardown released the tracked value and ran the cleanup |

Nothing in the test knew a port or a route. The registration decided which implementation ran, and the trace names it.

## Model the screen

The same idea on the browser side. `Pages.cs` describes the two screens the browser journey uses:

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
    {line: 3, title: 'One property, one element', note: 'A property resolves its element against the live page each time it is read, so a re-rendered screen does not leave a stale handle.'},
    {line: 16, title: 'A collection of components', note: 'Rows are their own component class, so a row can hold the name, the status and the environment count.'},
    {line: 18, title: 'Matching is strict', note: 'No match and more than one match are both errors. A filtered list either finds the one row or fails loudly.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Pages.cs</code>. The wait the journeys use lives beside it in <code>NorthstarPages.Wait</code>.</>}
/>

The journey then reads like a user:

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

Run it and read its trace:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~WebJourney"
```

The execution layer holds `web.navigate` for the open, `web.flow` for the sign-in flow and the filter flow, `web.fill` and `web.click` for the steps, and `assert.web` for the status check. Each entry carries the component path, from `ProjectsPage` down to the element the step touched, so a failure names the screen, the component and the element instead of a CSS selector.

If the journey skips, the reason names Playwright and the browser it could not find. The [web integration page](/docs/integrations/web/) covers the install and the probe. The unit of work is the same either way: the page object is ordinary code, and the run records what it touched.

</LearnShell>
