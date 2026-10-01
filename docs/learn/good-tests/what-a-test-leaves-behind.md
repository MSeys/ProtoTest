---
id: what-a-test-leaves-behind
title: Check that a test cleans up
sidebar_label: What a test leaves behind
sidebar_position: 6
description: "Read cleanup and resource-release evidence in a passing trace, and investigate missing or failed cleanup."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# Check that a test cleans up

<Lesson
  track="Write good integration tests"
  step="Lesson 6 of 7"
  minutes={7}
  outcomes={[
    'Say what a passing test owns and what removes it',
    'Find the cleanup and the release in a trace\'s teardown layer',
    'Identify cleanup that needs investigation',
  ]}
  needs={[
    <>Lesson 5, <Link to="/learn/good-tests/per-test-state-and-cleanup">Give each test its own state</Link></>,
    'Nothing installed. The archives are on this site',
  ]}
/>

## The problem

A test can pass and still leave unwanted data behind. In a shared store, that data can affect later tests or accumulate across runs. Decide who owns its cleanup.

This lesson follows the sample's tenant cleanup through the trace. It shows what the cleanup callback reported and what still needs checking outside the trace.

## Do it

### 1. Open the passing run

Download [l0-state-fix.prototrace](pathname:///lessons/l0-state-fix.prototrace) and open it in the [viewer](https://trace.prototest.dev). This is the same run as the last lesson. Last time you read setup and execution. Now read the end.

### 2. Read the teardown layer

Teardown is the phase after the test body. In this recording, it includes these operations:

- It publishes the attachments.
- It runs the tenant's cleanup callback: `data.cleanup` TenantResponse, 9.0 ms, with a successful outcome.
- It releases registered resources, including `data:TenantResponse:1`, application services, the database connection and the messaging consumer.

The tenant's `resource.release` contains its `data.cleanup` operation. They describe one release, not two separate deletions.

### 3. Check who owns what

Open the value list. The tenant item records `owned: true`. The project records `owned: false`: its provisioner returned a value without a separate cleanup callback.

The tenant provisioner returns a disposer that calls `NorthstarStore.DeleteTenant`. The database model cascades that deletion to the tenant's projects. ProtoTest registers the disposer as a resource owned by the test context.

The successful operation means the disposer returned without throwing. It is not an independent query proving that no records remain.

### 4. Picture the missing cleanup

If an expected release is absent or failed, inspect the resource registration and cleanup code. The process may have stopped before teardown, or cleanup may happen outside ProtoTest's recorded operations.
Check the store before concluding that data leaked. Conversely, a callback that succeeds without deleting anything can leave data behind.

Names containing a test id do not replace cleanup. The default run prefix is random, not guaranteed unique across processes. A fixed prefix also does not assign the same sequence number to a particular test across runs.

## What happened

ProtoTest releases resources registered as test-owned during teardown. Creating application data does not automatically register cleanup. Here, the tenant provisioner supplies it explicitly, and the trace records the callback's result.

The host releases run-owned resources after the tests finish. Lesson 2 showed three such release entries. A resource's owner determines when its cleanup runs.

## Check yourself

<Checkpoint
  question="The test creates a tenant and a project. The teardown has one data.cleanup entry, for the tenant. Where did the project go?"
  verify={<>Compare the setup and teardown layers of <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a>.</>}>

The tenant has the cleanup callback. Its deletion cascades to the project in this application's database model. The tenant does not own the database itself, and the project needs no separate disposer here.

</Checkpoint>

## Remember

- Register cleanup for data and resources the test owns.
- Read release outcomes, then check the store when the trace leaves cleanup uncertain.
- Test-owned resources release during teardown. Run-owned resources release at the end of the run.

## Go deeper

- [Host and lifecycle](/docs/foundation/lifecycle): what setup and teardown guarantee, and what happens when a step fails.
- Next: [When not to compose](/learn/good-tests/when-not-to-compose).
