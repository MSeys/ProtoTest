---
id: provisioners
title: Create test data with a provisioner
sidebar_label: Create test data
sidebar_position: 2
description: "Create test data through a provisioner, register it once, and read its chain in the trace."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# Create test data with a provisioner

<Lesson
  track="Extend ProtoTest"
  step="Lesson 2 of 6"
  minutes={6}
  outcomes={[
    'Write and register a provisioner',
    'Follow one provisioning call through its chain of trace entries',
  ]}
  needs={[
    <>The previous lesson, <a href="/learn/extend/attributes">Write your own attribute</a></>,
    'The sample cloned',
  ]}
/>

## The problem

A test that inserts rows directly skips the product's rules. A copied fixture drifts from its endpoint.

A provisioner creates an object through the door you choose and returns what the system gave back. You write it once, and every test asks for data by type.

## Do it

### 1. Read the shape

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

### 2. Register it

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

### 3. Use it and read the chain

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

## What happened

The provisioner holds how to create a fixture, in one named place. The test asks for a value by type, and the trace shows which implementation answered and how long each step took.

## Check yourself

<Checkpoint
  question="A provisioner returns a result with an identity and, optionally, a cleanup. What does the identity let another call do, and what happens to the cleanup when the test ends?"
  verify={<>Read the provisioner reference, then the release rows of the first journey's trace.</>}>

The identity lets a later call find the same value again, through <code>Ref&lt;T&gt;</code>. The cleanup runs at teardown, in reverse creation order, as a <code>data.cleanup</code> operation.

</Checkpoint>

## Remember

- A provisioner turns a built request into a created value and reports its identity.
- A defaults module fills the values every fixture of a kind shares.
- One data call writes a chain of entries: create, build, provision, then release and cleanup at teardown.

Next: [model a screen as a page object](/learn/extend/page-objects).

## Go deeper

- [Provisioners](/docs/integrations/data/provisioners): the contract, the identity map, cleanup and trace attributes.
