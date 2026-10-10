---
id: parallel-safety
title: Run .NET integration tests in parallel safely
sidebar_label: Parallel safety
sidebar_position: 3
description: "Find shared state that breaks .NET integration tests in parallel, then isolate test data with per-test tenants and owned cleanup."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';

# Run .NET integration tests in parallel safely

<Lesson
  track="Reliable tests"
  step="Lesson 3 of 4"
  minutes={7}
  outcomes={[
    'Spot the shared name that breaks tests in parallel',
    'Separate per-test state from shared objects',
    'See per-test tenants in two traces',
  ]}
  needs={[
    <>Per-test state and cleanup, from <a href="/learn/good-tests/per-test-state-and-cleanup">Keep state per test and clean it up</a></>,
    'The sample cloned to run the tests, or the two archives below to read their ownership records',
  ]}
/>

## The problem

Your suite passes when it runs one test at a time. You turn on parallelism to save time, and two tests now fail
at random. Run them alone and they pass.

Look for state the tests share. Two tests might create a project named `atlas` in the same tenant, the sample's space for one customer's data.
One might write a static field that another reads, or remove a record another still needs.
Running separately can hide those conflicts.

The sample enables parallelism. This lesson follows how it separates test data and assigns cleanup to the right owner.

## Do it

### 1. Turn parallelism on

```csharp
[assembly: LevelOfParallelism(8)]
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
```

These lines are in `samples/Northstar.ProtoTest/AssemblyInfo.cs`. They let NUnit schedule eligible tests with up to eight parallel workers.
NUnit decides which tests overlap. Each test gets its own fixture instance, but static fields and references to shared objects remain shared.

From the repository root, run the two tests whose saved traces appear below:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~EachTenantSeesOnlyItsOwnProjects|FullyQualifiedName~ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock"
```

Both tests pass. Compare their times in that run's trace to see whether they overlapped.

### 2. Give each test its own tenant

A tenant is the space where one test's durable records live. Its name carries the test id:

- The tenant name comes from `context.UniqueName("northstar")`, which reads `northstar-<test id>`.
- Names inside separate tenants can be fixed. `PlatformJourney` uses `atlas`, and `SheetsJourney` uses `report-atlas`. Their projects belong to the tenant each test creates.
- Member emails include `context.TestId`.
- Teardown deletes the tenant using the short name (slug) the application returned during setup.

The test id is unique within one host. Several processes sharing one database need an agreed naming scheme.

### 3. Keep per-test state on the context \{#3-keep-everything-else-on-the-context}

`Proto.Context` resolves the test context for the current async flow. Tasks that inherit that flow use the same context.
Keep those tasks within the test's lifetime. Do not pass a context between tests or store it in a static field.

The context keeps per-test state, clients, resources and evidence together. Registering a shared object there does not make it private or safe for concurrent use.

The run owns shared infrastructure, such as its store, broker and loopback listener. Tests use these resources but must not dispose them.

### 4. Compare two traces

Open these two archives in the [viewer](https://trace.prototest.dev), one per tab:

| Archive | Tenant | Test |
| --- | --- | --- |
| [l0-state-fix.prototrace](pathname:///lessons/l0-state-fix.prototrace) | `northstar-553135000001` | `EachTenantSeesOnlyItsOwnProjects` |
| [l3-clock-window.prototrace](pathname:///lessons/l3-clock-window.prototrace) | `northstar-725654000001` | `ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock` |

In each setup layer, find the tenant name carrying the test id. Then find its release during teardown.
The tenant resource's final state is `released` in both archives.

These archives come from separate runs. They show ownership and cleanup, not concurrent execution.

## What happened

The context and tenant address different kinds of shared state. The context associates values and evidence with a test.
The tenant separates that test's records in the database. Neither protects a shared mutable object that tests use directly.

Use these questions on your own tests:

- Can another test see or delete its records? Use a separate tenant or names unique within the shared system.
- Does it put per-test data in a static field or shared fixture? Move that data into the test context or a test-owned object.
- Does it start or dispose something the run should own, or the reverse? Fix the lifetime.
- Does its teardown remove everything it created? A missing release leaks more with every parallel run.

## Check yourself

<Checkpoint
  question="Two tests use the same project name in different tenants. What does the tenant isolate, and what does the test context do?"
  verify={<>Read the setup layers of both archives above. Each provisioned its tenant under a name carrying its own test id.</>}>

The tenant separates the projects in the database, so their names do not conflict across tenants.
The test context associates state and evidence with the test's async flow. It does not make a shared client, static field or database record private.

</Checkpoint>

## Remember

- Each test has its own context. Tasks within that test may share it.
- Separate tenants keep records apart. Shared mutable objects still need coordination.
- Release test-owned resources at teardown and run-owned resources after the run.

## Go deeper

- [Concurrency](/docs/foundation/concurrency): what flows with the context, what loses it, and how a suite opts in.
- Next lesson: [Wait for a read that lags a write](/learn/reliable-tests/wait-for-a-lagging-read).
