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

A test that assumes a record exists can depend on earlier tests or leftover data. The sample's state drill requests the project `prj_1` without creating it. The application answers 404, while the test expects 200. The drill is one of four in the [failure tour](/learn/understand-failures/a-failure-tour).

The fix creates a project in the test's own tenant, then checks that its list contains that project. You can follow the source excerpts and saved trace without running the sample.

## Do it

### 1. Give the test its own tenant

In this sample, a tenant groups one customer's data. `NorthstarTenantAttribute` creates one during test setup. `NorthstarMember` includes that attribute, so the state drill and its fix both receive a tenant:

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
    {line: 3, title: 'Provision through the data client', note: 'The registered provisioner decides how the tenant is created.'},
    {line: 5, title: 'Include the test id in the name', note: 'UniqueName returns "northstar-<test id>". A name is not an access boundary.'},
    {line: 8, title: 'Keep the tenant details in context', note: 'The ids and owner credentials stay with this test.'},
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
    {line: 2, title: 'Create in this test\'s tenant', note: 'The provisioner uses the signed-in member\'s tenant.'},
    {line: 3, title: 'Read the same tenant', note: 'The member\'s token makes the application list only this tenant\'s projects.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/FailureDrills.cs</code>. The drill next to it read <code>prj_1</code>.</>}
/>

The omitted NUnit assertions check `TotalCount == 1`, then compare the returned project's id and name with the values created above. Those checks prove that the list contains the expected project in this run.

### 3. Compare the drill and the fix

| | The drill | The fix |
| --- | --- | --- |
| Before the read | a tenant exists, but the test creates no project | `Create · CreateProjectRequest`, in the test's tenant |
| The call | `GET /api/v1/projects/prj_1`, HTTP 404 | `GET /api/v1/projects`, HTTP 200 |
| The check | expected 200, got 404 | the list holds one project, the one this test created |

### 4. Find the creation in the trace

Download [l0-state-fix.prototrace](pathname:///lessons/l0-state-fix.prototrace) and open it in the [viewer](https://trace.prototest.dev). Select `EachTenantSeesOnlyItsOwnProjects`. The Steps tab shows its operations phase by phase: Setup before the test body (folded into one line, so open it), then Execution for the body. The first word of each kind, such as `data` or `http`, says what sort of step it is:

| Layer | Entry | What it shows |
| --- | --- | --- |
| Setup | `data.create` ProvisionTenantRequest, 149.6 ms | the tenant `northstar-553135000001` exists before the body runs |
| Setup | `attribute.before` SignedInAs, then `auth.user.sign-in` | the test declares its identity |
| Execution | `data.create` CreateProjectRequest, 100.2 ms | the project, in the test's own tenant |
| Execution | `http.request` REST `GET /api/v1/projects`, 72.6 ms, HTTP 200 | the read the check judged |

Open the response attachment to see `totalCount: 1` and the project named `own-553135000001`.

## What happened

The test arranged its own tenant and project. Its authenticated request selected that tenant, and the application limited the list to that tenant's projects. The name helped identify the record, but did not provide the access boundary.

This holds under parallel runs too: each test keeps its own tenant credentials in its own context.

Cleanup is a separate step. Northstar's tenant provisioner returns a cleanup that deletes the tenant, and ProtoTest registers it as a test-owned resource. Lesson 6 follows it.

## Check yourself

<Checkpoint
  question="Why does EachTenantSeesOnlyItsOwnProjects find exactly one project, even when other tests run at the same time?"
  verify={<>In <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a>, read the setup entry that provisions the tenant and the execution entry that creates the project.</>}>

Setup creates a tenant, and the test creates one project inside it. Its authenticated list request reads that tenant's projects. Other tests using their own tenant credentials do not add projects to this tenant.

The assertions check the count, id and name. A test id in the name alone would not make a shared tenant safe.

</Checkpoint>

## Remember

- Arrange the data a test needs and check the returned identity. Do not assume a fixed id already exists.
- Use separate tenant state and credentials for isolation. Naming and cleanup are separate responsibilities.
- The setup layer of the trace shows what was provisioned. The execution layer shows what the test did with it.

## Go deeper

- [Parallel safety](/learn/reliable-tests/parallel-safety): how per-test ownership and scoped reads keep concurrent tests independent.
- Next: [What a test leaves behind](/learn/good-tests/what-a-test-leaves-behind).
