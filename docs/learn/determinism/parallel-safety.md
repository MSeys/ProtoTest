---
id: parallel-safety
title: Parallel safety
sidebar_label: Parallel safety
sidebar_position: 4
description: "Why the sample's tests can run in parallel: per-test names, per-test state, and one run that owns what tests share."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Parallel safety

The sample runs its tests eight at a time. Tests do not coordinate with each other. The rules that make a test deterministic also keep it isolated in parallel.

<LearnShell
  level="Level 3, lesson 4"
  minutes="About 8 minutes"
  outcome={[
    'Explain what makes a suite safe to run in parallel.',
    'Name the two rules a test follows to stay isolated.',
    'Spot the shared identifier that would break under parallel runs.',
  ]}
  before={[
    <>Keep state per test and clean it up (<Link to="/learn/determinism/per-test-state-and-cleanup">lesson 3</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>Parallelism turns every hidden share into a flaky failure. A name two tests both use, a static a test writes, a record one test reads after another removed it: none of them fail while the suite runs one test at a time.</p>
      <p>The sample asks for parallelism on purpose, so those shares have nowhere to hide. This lesson names what keeps the tests apart.</p>
    </>
  }
  checkpoint={{
    question:
      'The suite runs eight tests at once, and some journeys create fixed project names like `atlas`. Every test still passes. Which two mechanisms keep the tests apart?',
    verify: (
      <>
        Read the setup layers of <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a> and{' '}
        <a href="pathname:///lessons/l3-clock-window.prototrace">l3-clock-window.prototrace</a> in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>. Each provisioned its tenant under a name carrying its own test id.
      </>
    ),
    reveal: (
      <>
        The execution context is per test and flow-local, so one test cannot read another's clients, state or trace. And every test works inside its own tenant: the tenant name carries the test id, and fixed names like `atlas` live inside it, so two tests can use the same name without meeting. Isolation and the per-test tenant are the pair; either one alone leaves a way for tests to collide.
      </>
    ),
  }}
  learned={[
    'Parallel tests each get their own execution context, on their own async flow.',
    'The per-test tenant keeps durable records apart; per-test state keeps reads apart.',
    'The run owns what tests share; a test owns what it creates.',
  ]}
  next={[
    {
      label: 'Read a failing trace',
      to: '/learn/evidence/read-a-failing-trace',
      note: 'Level 4 starts with a failed run and the message that names the fix.',
    },
    {
      label: 'Concurrency',
      to: '/docs/foundation/concurrency',
      note: 'What flows with the context, what loses it, and the parallelism the project has exercised.',
    },
  ]}>

## The suite asks for it

<AnnotatedCode
  filename="AssemblyInfo.cs"
  code={`[assembly: LevelOfParallelism(8)]
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]`}
  callouts={[
    {line: 1, title: 'Eight workers', note: 'The suite runs up to eight tests at once.'},
    {line: 2, title: 'Tests and fixtures may run in parallel', note: 'The runner starts any test as soon as a worker is free.'},
    {line: 3, title: 'One fixture instance per test', note: 'Nothing test-scoped lives on a shared fixture instance.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/AssemblyInfo.cs</code>. A suite opts in when its tests isolate their own state; this one does.</>}
/>

## Rule one: a tenant per test

The tenant is what keeps durable records apart, and its name carries the test id:

- The tenant comes from `context.UniqueName("northstar")`, which reads `northstar-<test id>`.
- Names inside the tenant can be fixed. `atlas` and `report-atlas` are the same in every test, and no two of them meet, because no two tests share the tenant. A name only has to be unique where it can be seen.
- A record that is visible outside the tenant still carries the test id, because the run has no tenant to hide it in: the member email is built from `context.TestId`, and so is the scenario correlation id.
- The teardown removes the tenant by the identity setup recorded, not by a search for a name.

The test id carries the run prefix, so the same journey in the same second on two workers still produces two tenants. `TestId` is also what makes a rerun against a persistent store safe, unless a suite deliberately fixes the run prefix.

## Rule two: per-test state

The other half is what the context holds. Clients, state, resources, attachments and observations live on the test's execution context, which is scoped to its async flow. A test cannot read another test's context even while both run, so there is no shared object to guard and no lock to add.

What is shared lives in one place: the run. The store, the broker, the loopback listener and the report sinks are composed once, started once and released at the end. Parallel tests read them, and none of them owns them.

## The check to run on your own suite

Ask these of every test you write:

- Does it create a record outside its own tenant, or one another test can see, with a name that is not derived from the test? Replace the name with `UniqueName` or `TestId`.
- Does it write a static or a fixture field another test could read? Move the value into the context.
- Does it start or dispose something the run should own, or the reverse? Fix the lifetime, not the symptom.
- Does its teardown remove everything it created? If a release is missing, the leak grows with every parallel run.

## Where the evidence is

Each archive in this track is one test's trace, with its own tenant identity in the setup layer and its own cleanup in teardown. The two tenants below come from two committed archives, each named for the test id that provisioned it:

| Archive | Tenant | Test |
| --- | --- | --- |
| <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a> | `northstar-553135000001` | `EachTenantSeesOnlyItsOwnProjects` |
| <a href="pathname:///lessons/l3-clock-window.prototrace">l3-clock-window.prototrace</a> | `northstar-725654000001` | `ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock` |

Open the two archives side by side in the [viewer](https://trace.prototest.dev). Each setup layer provisions its tenant under a name carrying its own test id, each value list records that tenant with `owned: true`, and each teardown releases it. The fixed names inside the tenants (`atlas` in the coverage journey, `report-atlas` in the sheets journey) repeat across tests without meeting, because no two tests share the tenant. The [concurrency page](/docs/foundation/concurrency) covers the mechanics: what flows with the context, what loses it, and how a suite opts in.

</LearnShell>