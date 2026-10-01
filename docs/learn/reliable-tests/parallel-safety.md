---
id: parallel-safety
title: Parallel safety
sidebar_label: Parallel safety
sidebar_position: 3
description: "Find the shared name that makes tests break each other in parallel, and see how per-test tenants and per-test state keep them apart."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Parallel safety

<Lesson
  track="Reliable tests"
  step="Lesson 3 of 3"
  minutes={7}
  outcomes={[
    'Spot the shared name that breaks tests in parallel',
    'Name the two rules that keep a test isolated',
    'See per-test tenants in two traces',
  ]}
  needs={[<>Per-test state and cleanup, from <a href="/learn/good-tests/per-test-state-and-cleanup">Keep state per test and clean it up</a></>]}
/>

## The problem

Your suite passes when it runs one test at a time. You turn on parallelism to save time, and two tests now fail
at random. Run them alone and they pass.

The cause is almost always a hidden share. Two tests create a project named `atlas`. One test writes a static
field that another reads. One test removes a record that another still needs. Nothing fails in a serial run,
because the tests never overlap.

The sample asks for parallelism on purpose, so those shares have nowhere to hide.

## Do it

### 1. Turn parallelism on

```csharp
[assembly: LevelOfParallelism(8)]
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
```

These lines are in `samples/Northstar.ProtoTest/AssemblyInfo.cs`. The suite runs up to eight tests at once, and
the runner starts any test as soon as a worker is free. Each test gets its own fixture instance, so nothing
test-scoped lives on a shared object. Turn this on only when your tests isolate their own state, as this suite does.

### 2. Give each test its own tenant

A tenant is the space where one test's durable records live. Its name carries the test id:

- The tenant name comes from `context.UniqueName("northstar")`, which reads `northstar-<test id>`.
- Names inside the tenant can be fixed. `atlas` and `report-atlas` are the same in every test, and no two meet, because no two tests share the tenant. A name only has to be unique where it can be seen.
- A record visible outside the tenant still carries the test id. The member email is built from `context.TestId`, and so is the scenario correlation id.
- The teardown removes the tenant by the identity that setup recorded, not by searching for a name.

The test id carries the run prefix, so the same journey in the same second on two workers still gets two tenants.

### 3. Keep everything else on the context

Clients, state, resources, attachments and observations live on the test's execution context. The context is
scoped to its async flow, so a test cannot read another test's context while both run. There is no shared object
to guard and no lock to add.

What tests do share lives in one place, the run. The store, the broker, the loopback listener and the report
sinks are started once and released at the end. Tests read them, and none of them owns them.

### 4. Compare two traces

Open these two archives in the [viewer](https://trace.prototest.dev), one per tab:

| Archive | Tenant | Test |
| --- | --- | --- |
| [l0-state-fix.prototrace](pathname:///lessons/l0-state-fix.prototrace) | `northstar-553135000001` | `EachTenantSeesOnlyItsOwnProjects` |
| [l3-clock-window.prototrace](pathname:///lessons/l3-clock-window.prototrace) | `northstar-725654000001` | `ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock` |

In each setup layer you should see a tenant provisioned under a name with its own test id. The value list records
that tenant with `owned: true`, and the teardown releases it. The fixed names inside, `atlas` in the coverage
journey and `report-atlas` in the sheets journey, repeat across tests without meeting.

## What happened

Two rules kept the tests apart. The context is per test, so one test cannot see another's clients, state or trace.
The tenant is per test, so fixed names inside it never collide. Either rule alone leaves a way to collide: the
context does not protect a record in a shared database, and a tenant does not protect a static field.

Use these questions on your own tests:

- Does it create a record another test can see, with a name that is not derived from the test? Use `UniqueName` or `TestId`.
- Does it write a static or a fixture field another test could read? Move the value into the context.
- Does it start or dispose something the run should own, or the reverse? Fix the lifetime.
- Does its teardown remove everything it created? A missing release leaks more with every parallel run.

## Check yourself

<Checkpoint
  question="The suite runs eight tests at once, and some journeys create fixed project names like `atlas`. Every test still passes. Which two mechanisms keep the tests apart?"
  verify={<>Read the setup layers of both archives above. Each provisioned its tenant under a name carrying its own test id.</>}>

The execution context is per test and flow-local, so one test cannot read another's clients, state or trace. And
every test works inside its own tenant. The tenant name carries the test id, and fixed names like `atlas` live
inside it, so two tests can use the same name without meeting. Either one alone leaves a way for tests to collide.

</Checkpoint>

## Remember

- Each parallel test gets its own execution context, on its own async flow.
- The per-test tenant keeps durable records apart. Per-test state keeps reads apart.
- The run owns what tests share. A test owns what it creates.

## Go deeper

- [Concurrency](/docs/foundation/concurrency): what flows with the context, what loses it, and how a suite opts in.
- Next track: [Read a failing trace](/learn/understand-failures/read-a-failing-trace).
