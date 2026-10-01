---
id: what-a-test-leaves-behind
title: Check that a test cleans up
sidebar_label: What a test leaves behind
sidebar_position: 6
description: "Read the teardown layer of a passing trace to see what a test removed, and recognize a leak when the cleanup is missing."
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
    'Recognize what a leak looks like',
  ]}
  needs={[
    <>Lesson 5, <Link to="/learn/good-tests/per-test-state-and-cleanup">Give each test its own state</Link></>,
    'Nothing installed. The archives are on this site',
  ]}
/>

## The problem

A test that creates its own data is only half done. If the data stays in the store, the next run starts with it still there. Reads then depend on the order tests ran in, and a green suite turns red without a code change.

A passing test also has to prove it removed what it created. The trace records that, and this lesson shows where to look.

## Do it

### 1. Open the passing run

Download [l0-state-fix.prototrace](pathname:///lessons/l0-state-fix.prototrace) and open it in the [viewer](https://trace.prototest.dev). This is the same run as the last lesson. Last time you read setup and execution. Now read the end.

### 2. Read the teardown layer

Teardown is the phase after the test body, where the test releases what it used. It does three things in this run:

- It publishes the attachments.
- It removes the tenant: `data.cleanup` TenantResponse, 9.0 ms.
- It releases the resources the context owned: `resource.release` of `data:TenantResponse:1`, each with its own release entry.

### 3. Check who owns what

Open the value list. The tenant item records `owned: true`. The project is a value the test read, not an owned resource.

There is one cleanup for the tenant and none for the project. Deleting the tenant removes everything created inside it, the project included. The provisioner returned the tenant together with a disposer, and that is what makes the run own it.

### 4. Picture the missing cleanup

If the cleanup were missing, you would see the setup's `data.create` entry but no `data.cleanup` and no `resource.release` for the tenant in the teardown layer. The record would stay in the store.

The run prefix changes per run, so the next run would provision a fresh tenant. The store still grows. A leak collides when a suite fixes its run prefix or points at a shared environment. The trace shows it before that happens.

## What happened

Each test owns the state it creates and removes it on the way out. The trace records the removal, so a missing one is visible in the teardown layer. A release that never appears is the signal, which is why teardown is worth reading even when a test passes.

The host does the same for the run. In lesson 2 you saw three `Release` entries at the end of the run, for the pieces no test owned. Test state is released by the test's teardown. Run state is released by the host.

## Check yourself

<Checkpoint
  question="The test creates a tenant and a project. The teardown has one data.cleanup entry, for the tenant. Where did the project go?"
  verify={<>Compare the setup and teardown layers of <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a>.</>}>

The tenant is the owned resource, and it owns the store the project lives in. Deleting the tenant removes the project with it. The test asked for the project, and the tenant carries the cleanup.

</Checkpoint>

## Remember

- A test owns the state it creates and removes it on the way out.
- The teardown layer is where you check that it did: look for the cleanup and the release.
- Test state is released by the test. Run state is released by the host.

## Go deeper

- [Host and lifecycle](/docs/foundation/lifecycle): what setup and teardown guarantee, and what happens when a step fails.
- Next: [When not to compose](/learn/good-tests/when-not-to-compose).
