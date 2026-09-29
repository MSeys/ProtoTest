---
id: attributes
title: Write your own attribute
sidebar_label: Attributes
sidebar_position: 1
description: "Turn setup into a named attribute, compose it with others, and read the before and after entries it leaves in the trace."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Write your own attribute

Level 6 returns to the Northstar sample. The first journey already carries `[NorthstarMember]`, and that one line provisions a tenant and signs a member in. This lesson writes the next attribute yourself.

<LearnShell
  level="Level 6, lesson 1"
  minutes="About 8 minutes"
  outcome={[
    'Derive an attribute from ProtoAttribute and override its setup.',
    'Group attributes that always travel together into a composite.',
    'Find every attribute in the trace, before and after.',
  ]}
  before={[
    <>Inject faults on purpose (<Link to="/learn/real-topology/fault-injection">Level 5, lesson 4</Link>).</>,
    'The sample cloned and open in an editor.',
  ]}
  situation={
    <>
      <p>A test declares what it needs. An attribute provides it. That split is why the sample's journeys carry one declaration each instead of a base fixture class full of setup.</p>
      <p>The attributes the sample ships are ordinary public code. This lesson reads one and then writes one.</p>
    </>
  }
  checkpoint={{
    question:
      'The tenant attribute declares Order -200 and your new attribute keeps the default 0. Of those two, which before entry comes first, and in what order do their after entries appear in teardown?',
    verify: (
      <>
        Run the filtered test and open its trace. Read the setup phase, then the teardown phase, and compare the two orders.
      </>
    ),
    reveal: (
      <>
        Attributes run in ascending Order before the test and in reverse afterwards. The tenant attribute at -200 runs first, so its before entry comes before yours in the setup phase, and its after entry comes after yours in the teardown phase.
      </>
    ),
  }}
  learned={[
    'An attribute derives from ProtoAttribute and overrides BeforeTestAsync and AfterTestAsync.',
    'Order sequences attributes that depend on each other, and teardown runs in reverse.',
    'Every attribute gets its own before and after entry, so the trace shows what actually executed.',
  ]}
  next={[
    {
      label: 'Provisioners and page objects',
      to: '/learn/make-it-yours/provisioners-and-page-objects',
      note: 'Where the data an attribute provisions comes from, and how a test reads a screen.',
    },
    {
      label: 'Attributes',
      to: '/docs/foundation/attributes',
      note: 'The full contract: ordering, composites, skip conditions and the trace view.',
    },
  ]}>

## The sample's attribute

`NorthstarTenantAttribute` provisions an isolated organization for the test and removes it afterwards:

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
    {line: 2, title: 'Derive, do not configure', note: 'The class is the capability. Nothing registers it; declaring it on a test is the composition.'},
    {line: 8, title: 'Order says what it must precede', note: 'A lower order runs earlier. Provisioning a tenant must precede the signed-in identity, so this is negative and the member identity stays at 0.'},
    {line: 15, title: 'Provision through the data surface', note: 'The attribute does not open a connection or build a URL. It asks the data surface for a tenant, and the registered provisioner decides how that is done.'},
    {line: 20, title: 'Publish typed state', note: 'SetContext makes the tenant available to tests, authenticators and other attributes, and the value is recorded as traced state.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarAttributes.cs</code>. The companion <code>NorthstarMemberAttribute</code> is a composite: it declares a tenant attribute and the signed-in identity together.</>}
/>

The composite is the part that keeps a test declaration short:

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

A composite expands into the attributes it names, in the same ordering rules. The trace shows both: each composed attribute has its own entries, and the composite's entries carry the types it expanded to.

## Write one

Add a file to the sample project, `RunNoteAttribute.cs`:

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

Apply it to a test. The file from Level 1 works; if you removed it there, [Write your first test](/learn/one-test-one-journey/write-your-first-test) has the snippet to recreate it, or apply the attribute to any journey in the sample:

```csharp
[Application(NorthstarTargets.Api)]
[NorthstarMember]
[RunNote("first attribute")]
public sealed class MyFirstJourney
```

Run it alone:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
```

Open `bin/Debug/net8.0/TestResults/Northstar.ProtoTest/northstar.prototrace` under the sample project in the [viewer](https://trace.prototest.dev). The setup phase holds a `Before · RunNoteAttribute` entry and the event you wrote, and the teardown phase holds the matching `After` entry.

## The evidence the sample leaves

The committed archive for the first journey, <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, shows the same mechanism, with a real provisioning chain inside the attribute:

| Entry | Reading |
| --- | --- |
| `Before · NorthstarTenantAttribute`, 139.9 ms | the attribute runs first, at Order -200 |
| `Create · ProvisionTenantRequest`, 137.0 ms, with `Build` and `Provision` below it | the data surface builds the request, creates it and returns the response |
| `Before · NorthstarMemberAttribute` | the composite's own entry, carrying the attributes it composed |
| `Apply · NorthstarAuthenticator`, 1 ms | the HTTP auth hook applies the member's token on the first call |
| `After · NorthstarMemberAttribute`, then `After · NorthstarTenantAttribute`, with `data.cleanup Cleanup · TenantResponse` | teardown reverses the order and the provisioned tenant is removed |

That is the whole attribute contract in one page: a declaration on a test, a before step, typed state, an after step, and a trace that shows each one.

</LearnShell>
