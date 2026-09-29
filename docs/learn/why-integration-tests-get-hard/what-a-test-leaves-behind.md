---
id: what-a-test-leaves-behind
title: What a test leaves behind
sidebar_label: What a test leaves behind
sidebar_position: 3
description: "What a passing integration test owns, what it removes, and where the trace shows the cleanup."
---

import LearnShell from '@site/src/components/LearnShell';
import Link from '@docusaurus/Link';

# What a test leaves behind

A test that reads state it did not create depends on every run before it. That is the state question from lesson 1, and it is the one that turns a green suite red on a Tuesday morning.

<LearnShell
  level="Level 0, lesson 3"
  minutes="About 8 minutes"
  outcome={[
    'Say what a passing test owns and what it removes.',
    'Find the cleanup a run recorded in its trace.',
    'Explain why leftovers make failures depend on run order.',
  ]}
  before={[
    <>A failure tour (<Link to="/learn/why-integration-tests-get-hard/a-failure-tour">lesson 2</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>The state drill asks for the project id <code>prj_1</code> and gets a 404. The request is valid, the id looks valid, and no test in that run created the project. The drill did not trip over a bug; it read state that belonged to no one.</p>
      <p>The paired fix, <code>EachTenantSeesOnlyItsOwnProjects</code>, creates a project first and then lists what its own tenant can see. It finds exactly one project: the one it created.</p>
    </>
  }
  checkpoint={{
    question:
      'The state fix provisions a tenant in setup and removes it in teardown. If the removal were missing, what would the trace show, and what would the next run see?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a>, open it in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, and read its teardown layer next to its setup layer. The setup creates the tenant; the teardown removes it.
      </>
    ),
    reveal: (
      <>
        Without the cleanup, the teardown layer would hold no <code>data.cleanup</code> entry and the tenant would stay in the store. The next run would see state it did not create, and what it reads would depend on what ran before it. The trace records the cleanup, so a missing one is visible in the same place.
      </>
    ),
  }}
  learned={[
    'A test owns the state it creates and removes it on the way out.',
    'The teardown layer of the trace is where you check that it did.',
    'Names built from the test id are what keep parallel tests from colliding.',
  ]}
  next={[
    {
      label: 'The trace as a feedback loop',
      to: '/learn/why-integration-tests-get-hard/the-trace-as-the-feedback-loop',
      note: 'Read a failed run without rerunning it, and narrow the fix one question at a time.',
    },
    {
      label: 'Host and lifecycle',
      to: '/docs/foundation/lifecycle',
      note: 'What setup and teardown guarantee, and what happens when a step fails.',
    },
  ]}>

## The pair, side by side

The drill reads and the request 404s. The fix creates and then reads:

| | The drill | The fix |
| --- | --- | --- |
| Before the read | nothing | `Create · CreateProjectRequest`, provisioned in the test tenant |
| The call | `GET /api/v1/projects/prj_1`, HTTP 404 | `GET /api/v1/projects`, HTTP 200 |
| The check | expected 200, got 404 | the list holds one project, the one this test created |

The fix does not look up a well known id or seed a fixture in advance. It creates its own project, reads it, and lets teardown remove the tenant that holds it.

## A passing run, end to end

Read the fix's trace in three parts:

- **Setup** opens the connection (`sql.connection.open`), provisions the tenant (`data.create · ProvisionTenantRequest`, then `data.provision`), and signs the member in. The tenant belongs to this test and no other.
- **Execution** creates the project through the data client (`data.create · CreateProjectRequest`), reaches the application (`project.create`, reported by the application itself), and reads the list back over REST, with the status check on the response.
- **Teardown** publishes the attachments, removes the tenant (`data.cleanup · TenantResponse`), and releases the resources the context owned, each with its own release entry.

A resource that is never released would show up as a missing release entry in that last part. That is why the teardown layer is worth reading even when a test passes.

## Names that keep tests apart

The sample runs its tests in parallel, eight at a time, and no identifier it creates can collide across tests:

- The tenant name comes from `context.UniqueName("northstar")`, so each test owns its own tenant.
- Project names are fixed inside that tenant, like `atlas`, or carry `Proto.Context.TestId`; either way no other test can reach them.
- The teardown removes the tenant by the identity the setup recorded, not by a search.

That is what makes the state answer hold under a parallel run: the test reads only what it created, and what it created lives in a tenant no other test can reach.

## What a leak looks like

If the cleanup were missing, the trace would still hold the setup's `data.create` entry, and the teardown layer would not hold `data.cleanup` or the `resource.release` line for the tenant. The next run would start with that tenant still in the store. Reads would then depend on the order tests happen to run in, which is exactly the kind of failure that looks random.

</LearnShell>
