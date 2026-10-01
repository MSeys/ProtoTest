---
id: attributes
title: Write your own attribute
sidebar_label: Attributes
sidebar_position: 1
description: "Turn repeated test setup into a named attribute, group attributes into one, and find their before and after entries in the trace."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# Write your own attribute

<Lesson
  track="Extend ProtoTest"
  step="Lesson 1 of 6"
  minutes={8}
  outcomes={[
    'Write an attribute that prepares something for a test and cleans it up',
    'Group attributes that always travel together into one',
    'Find each attribute\'s before and after entries in the trace',
  ]}
  needs={[
    'The tracks up to "Write good integration tests"',
    'The sample cloned and open in an editor',
  ]}
/>

## The problem

Every test in your project starts with the same setup: create an organization, sign a user in, note why the test exists. You could copy that code into each test or hide it in a base class. Both get heavy as the suite grows.

An attribute is a C# attribute that prepares something for a test and cleans it up afterwards. The test declares what it needs, and the attribute provides it. The sample's journeys carry one line, `[NorthstarMember]`, and that line creates a tenant and signs a member in.

## Do it

### 1. Read the sample's attribute

`NorthstarTenantAttribute` creates an isolated organization for the test. The data surface it calls, `context.Data()`, creates test data and registers its cleanup, so the attribute does not remove the tenant itself.

<AnnotatedCode
  filename="NorthstarAttributes.cs"
  code={`[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class NorthstarTenantAttribute : ProtoAttribute
{
    public NorthstarTenantAttribute(string planId = PlanIds.Free)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        PlanId = planId;
        Order = -200;
    }

    public string PlanId { get; }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var tenant = await context.Data()
            .For<ProvisionTenantRequest>()
            .With(request => request.Name, context.UniqueName("northstar"))
            .With(request => request.PlanId, PlanId)
            .CreateAsync<TenantResponse>();
        context.SetContext(new NorthstarOrganizationContext(
            tenant.Tenant,
            tenant.OrganizationId,
            tenant.OwnerEmail,
            tenant.OwnerToken,
            tenant.ApiBaseUrl));
    }
}`}
  callouts={[
    {line: 2, title: 'Derive, do not configure', note: 'Nothing registers the class. Putting it on a test is the whole wiring.'},
    {line: 8, title: 'Order says what runs first', note: 'A lower order runs earlier. The tenant must exist before the signed-in member, so this is negative and the member stays at 0.'},
    {line: 15, title: 'Ask for data, not for a connection', note: 'The attribute asks the data surface for a tenant. A registered provisioner decides how it is created (the next lesson).'},
    {line: 20, title: 'Publish the result', note: 'SetContext makes the tenant available to tests and to other attributes, and the trace records it.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarAttributes.cs</code>.</>}
/>

### 2. See how attributes group

Most tests need a tenant and a signed-in user together. `NorthstarMemberAttribute` is a composite attribute that names both, so a test writes one line:

```csharp
public sealed class NorthstarMemberAttribute(string planId = PlanIds.Free) : ProtoCompositeAttribute
{
    public string PlanId { get; } = planId;

    protected override IReadOnlyList<Attribute> Compose() =>
    [
        new NorthstarTenantAttribute(PlanId),
        new AuthAttribute<NorthstarAuthenticator>(),
    ];
}
```

### 3. Write your own

Add `RunNoteAttribute.cs` to the sample project. It writes a note into the trace, so a run carries its reason next to its evidence:

```csharp
namespace Northstar.ProtoTest;

using global::ProtoTest.Core;

/// <summary>Records why this test exists, so a run carries its reason next to its evidence.</summary>
public sealed class RunNoteAttribute(string note) : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        context.Trace.WriteEvent("run.note", note, "Northstar.ProtoTest");
        return Task.CompletedTask;
    }
}
```

### 4. Apply it and run it

Use the first test from [Write your first test](/learn/start/write-your-first-test), or add the attribute to any journey in the sample:

```csharp
[Application(NorthstarTargets.Api)]
[NorthstarMember]
[RunNote("first attribute")]
public sealed class MyFirstJourney
```

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
```

Open `bin/Debug/net8.0/TestResults/prototest-{runId}.prototrace` under the sample project in the [viewer](https://trace.prototest.dev). The setup phase holds a `Before · RunNoteAttribute` entry and the event you wrote. The teardown phase holds the matching `After` entry.

## What happened

The host found your attribute on the test and ran its `BeforeTestAsync` before the test body. Attributes run in ascending `Order` before the test, and in reverse afterwards. The last thing set up is the first thing cleaned up.

The committed trace of the first journey, <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, shows the sample's attributes the same way:

| Entry | Reading |
| --- | --- |
| `Before · NorthstarTenantAttribute`, 139.9 ms | Runs first, at order -200. |
| `Create · ProvisionTenantRequest`, 137.0 ms | The tenant attribute's call to the data surface. |
| `Before · NorthstarMemberAttribute` | The composite's own entry, carrying the attributes it composed. |
| `After · NorthstarMemberAttribute`, then `After · NorthstarTenantAttribute` | Teardown reverses the order. |
| `Release · data:TenantResponse:1`, then `Cleanup · TenantResponse` | The data surface removes the tenant it created. |

## Check yourself

<Checkpoint
  question="The tenant attribute declares Order -200 and your new attribute keeps the default 0. Which before entry comes first, and in what order do their after entries appear in teardown?"
  verify={<>Run the filtered test and open its trace. Compare the setup phase with the teardown phase.</>}>

The tenant attribute's before entry comes first, so it appears before yours in the setup phase. Teardown runs in reverse, so its after entry comes after yours.

</Checkpoint>

## Remember

- An attribute derives from `ProtoAttribute` and overrides `BeforeTestAsync`, and `AfterTestAsync` when it has something to undo.
- `Order` sequences attributes that depend on each other, and teardown runs in reverse.
- A composite attribute groups attributes that always travel together.

Next: [create test data with a provisioner](/learn/extend/provisioners), where the data an attribute creates comes from.

## Go deeper

- [Attributes](/docs/foundation/attributes): ordering, composites, skip conditions and the trace view.
