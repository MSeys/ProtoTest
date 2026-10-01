---
id: per-test-state-and-cleanup
title: Give each test its own state
sidebar_label: Give each test its own state
sidebar_position: 5
description: "Provision the data a test reads under a name built from its test id, and read only what the test created."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Give each test its own state

<Lesson
  track="Write good integration tests"
  step="Lesson 5 of 7"
  minutes={8}
  outcomes={[
    'Create the data a test needs under a name built from its test id',
    'Read only the data the test created',
    'Find the creation in the trace\'s setup and execution layers',
  ]}
  needs={[
    <>Lesson 4, <Link to="/learn/good-tests/sign-in-as-a-test-user">Sign in as a test user</Link></>,
    'Nothing installed. The archives are on this site',
  ]}
/>

## The problem

A test that reads data it did not create depends on whatever ran before it. In the sample's state drill, a test asks for the project `prj_1`. The request is valid and the id looks valid, but no test in that run created it, so the application answers 404. The drill is one of four in the [failure tour](/learn/understand-failures/a-failure-tour).

The fix is to create the data the test needs and read only that. This lesson shows how the sample does it.

## Do it

### 1. Give the test its own tenant

In the sample the unit of isolation is the tenant. An attribute provisions one before each test, using a name taken from the test:

<AnnotatedCode
  filename="NorthstarAttributes.cs"
  code={`public override async Task BeforeTestAsync(ProtoExecutionContext context)
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
}`}
  callouts={[
    {line: 3, title: 'Provision through the data client', note: 'The request says what the test needs. The registered provisioner decides how it is created.'},
    {line: 5, title: 'Name it from the test', note: 'UniqueName turns "northstar" into "northstar-<test id>", so no other test can share the record.'},
    {line: 8, title: 'Hand the result to the test', note: 'The tenant lands in the test context, where the test and its clients read it.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarAttributes.cs</code>.</>}
/>

### 2. Create the data inside it, and read it back

`EachTenantSeesOnlyItsOwnProjects` creates a project, then lists the projects its tenant can see:

<AnnotatedCode
  filename="FailureDrills.cs"
  code={`var name = $"own-{Proto.Context.TestId}";
var project = await Proto.Context.Data().CreateProjectAsync(name);

using var response = await Proto.Context.Rest().GetAsync("/api/v1/projects");
var page = response
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .ReadRequired<CursorPage<ProjectResponse>>();`}
  callouts={[
    {line: 1, title: 'Create with the test id', note: 'The name carries TestId, so the record belongs to this test.'},
    {line: 4, title: 'Read only your own tenant', note: 'The list call runs inside the provisioned tenant, so it returns the one project this test created.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/FailureDrills.cs</code>. The drill next to it read <code>prj_1</code>.</>}
/>

The list holds one project, and it is the one the test just created.

### 3. Compare the drill and the fix

| | The drill | The fix |
| --- | --- | --- |
| Before the read | nothing | `Create · CreateProjectRequest`, in the test's tenant |
| The call | `GET /api/v1/projects/prj_1`, HTTP 404 | `GET /api/v1/projects`, HTTP 200 |
| The check | expected 200, got 404 | the list holds one project, the one this test created |

### 4. Find the creation in the trace

Download [l0-state-fix.prototrace](pathname:///lessons/l0-state-fix.prototrace) and open it in the [viewer](https://trace.prototest.dev). Look at the first two layers:

| Layer | Entry | What it shows |
| --- | --- | --- |
| Setup | `data.create` ProvisionTenantRequest, 149.6 ms, then `data.provision`, 144.6 ms | the tenant exists before the body runs, named `northstar-553135000001` in this recording |
| Setup | `attribute.before` SignedInAs, then `auth.user.sign-in` | the member acts inside that tenant |
| Execution | `data.create` CreateProjectRequest, 100.2 ms | the project is created in the test's own tenant |
| Execution | `http.request` REST `GET /api/v1/projects`, 72.6 ms, HTTP 200 | the read the check judged |

## What happened

The test never looked up a well known id and never relied on a fixture seeded in advance. An attribute created the tenant, the test created the project inside it, and the read could only see that tenant.

The unique name is what makes this safe in parallel. The sample runs eight tests at a time. The tenant name comes from `UniqueName`, project names are fixed inside a tenant or carry `TestId`, and no other test can reach that tenant. Lesson 6 covers what happens to the tenant afterwards.

## Check yourself

<Checkpoint
  question="Why does EachTenantSeesOnlyItsOwnProjects find exactly one project, even when other tests run at the same time?"
  verify={<>In <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a>, read the setup entry that provisions the tenant and the execution entry that creates the project.</>}>

The test's own tenant is created from a name that carries its test id, and the list call runs inside that tenant. Other tests work in their own tenants, so nothing they create is visible here.

</Checkpoint>

## Remember

- Create the data a test reads, in that test, under a name built from its test id.
- Read only what the test created. A fixed id belongs to nobody.
- The setup layer of the trace shows what was provisioned. The execution layer shows what the test did with it.

## Go deeper

- [Parallel safety](/learn/reliable-tests/parallel-safety): what keeps these names apart when eight tests run at once.
- Next: [What a test leaves behind](/learn/good-tests/what-a-test-leaves-behind).
