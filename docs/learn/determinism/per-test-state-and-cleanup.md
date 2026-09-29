---
id: per-test-state-and-cleanup
title: Keep state per test and clean it up
sidebar_label: Keep state per test and clean it up
sidebar_position: 3
description: "Provision the state a test reads, remove it at teardown, and read the ownership in the trace."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Keep state per test and clean it up

The state drill from Level 0 reads a project id that no test in the run created (`prj_1`, a fixed id that belongs to no tenant), and the request 404s. That drill is one of the four in [the failure tour](/learn/why-integration-tests-get-hard/a-failure-tour). The fix creates its own state and reads only that. This lesson reads the fix in the trace, including what the run owns and removes.

<LearnShell
  level="Level 3, lesson 3"
  minutes="About 8 minutes"
  outcome={[
    'Provision state in setup under a name that carries the test id.',
    'Read only the state the test created.',
    'Find the cleanup and the release that prove the state was removed.',
  ]}
  before={[
    <>Wait for readiness, not for time (<Link to="/learn/determinism/readiness-instead-of-sleeps">lesson 2</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>A test that reads state it did not create depends on whatever ran before it. The fix for the state question is short: create the data the test needs, read it, and let teardown remove it.</p>
      <p>In the sample the unit of isolation is the tenant. An attribute provisions one per test, the test works inside it, and teardown deletes it. The project the test creates lives inside that tenant and goes with it.</p>
    </>
  }
  checkpoint={{
    question:
      'The trace lists the tenant as an owned value and records its cleanup and release. The project the test created is not released on its own. Where does it go?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a>, open it in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, and compare the setup and teardown layers.
      </>
    ),
    reveal: (
      <>
        The tenant is the owned resource. Deleting it removes everything created inside it, the project included. That is why the teardown holds one <code>data.cleanup</code> entry for the tenant and none for the project: the test asked for the project, and the tenant owns the store it lives in.
      </>
    ),
  }}
  learned={[
    'A test provisions the state it reads, under a name built from its own test id.',
    'The owned resource is the tenant; the data inside it is removed with it.',
    'Teardown records each cleanup and release, so a missing one is visible in the same place.',
  ]}
  next={[
    {
      label: 'Parallel safety',
      to: '/learn/determinism/parallel-safety',
      note: 'What keeps these names and resources apart when eight tests run at once.',
    },
    {
      label: 'Lifecycle',
      to: '/docs/foundation/lifecycle',
      note: 'Setup, execution and teardown, and what happens when one of them fails.',
    },
  ]}>

## Where the tenant comes from

The attribute that provisions it uses a name from the test:

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
    {line: 3, title: 'Provision through the data client', note: 'The request says what the test needs; the registered provisioner decides how it is created.'},
    {line: 5, title: 'Name it from the test id', note: 'UniqueName turns "northstar" into "northstar-<test id>", so no other test can share the record.'},
    {line: 8, title: 'Hand the result to the test', note: 'The provisioned tenant lands in the execution context, where the test and its clients read it.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarAttributes.cs</code>. The provisioner returns the tenant with a disposer, which is what makes the run own it.</>}
/>

## The journey that reads it

`EachTenantSeesOnlyItsOwnProjects` creates a project and lists the projects its tenant can see:

<AnnotatedCode
  filename="FailureDrills.cs"
  code={`var name = $"own-{Proto.Context.TestId}";
var project = await Proto.Context.Data().CreateProjectAsync(name);

using var response = await Proto.Context.Rest().GetAsync("/api/v1/projects");
var page = response
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .ReadRequired<CursorPage<ProjectResponse>>();`}
  callouts={[
    {line: 1, title: 'Create with the test id', note: 'The name carries TestId, so the record belongs to this test even though the project name itself is ordinary.'},
    {line: 3, title: 'Read only the own tenant', note: 'The list call runs inside the provisioned tenant, so it returns the one project this test created.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/FailureDrills.cs</code>. The drill next to it read <code>prj_1</code>, a fixed id that belonged to no test in the run.</>}
/>

The list holds one project, and it is the one the test just created. The drill next to it read `prj_1`, a fixed id that belonged to no test in the run, and the check failed on the 404 the application answered.

## The two layers to read

From `l0-state-fix.prototrace`:

| Layer | Entry | What it proves |
| --- | --- | --- |
| Setup | `data.create` ProvisionTenantRequest, 149.6 ms, then `data.provision`, 144.6 ms | the tenant exists before the body runs, named `northstar-553135000001` in this recording |
| Setup | `attribute.before` SignedInAs, then its `auth.user.sign-in` event | the member acts inside that tenant |
| Execution | `data.create` CreateProjectRequest, 100.2 ms | the project is created under the test's own tenant |
| Execution | `http.request` REST `GET /api/v1/projects`, 72.6 ms, HTTP 200 | the read the check judged |
| Teardown | `data.cleanup` TenantResponse, 9.0 ms, and `resource.release` of `data:TenantResponse:1` | the owned state is removed, and the release is recorded |

The value list tells the same story: the tenant item records `owned: true`, and the project is a value the test read. One cleanup covers both.

## What a leak would look like

If the cleanup were missing, the teardown layer would hold no `data.cleanup` and no release for the tenant, and the record would stay in the store. The run prefix changes per run, so the next run provisions a fresh tenant. The leak still grows the store. It collides when a suite fixes its run prefix or points at a shared environment. The trace is where the leak is visible before that happens.

</LearnShell>