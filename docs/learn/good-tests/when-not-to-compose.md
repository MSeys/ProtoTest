---
id: when-not-to-compose
title: Decide what belongs in the host
sidebar_label: When not to compose
sidebar_position: 7
description: "Decide whether a piece belongs to the host, to a test, or to neither, and recognize the three common ways suites get it wrong."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# Decide what belongs in the host

<Lesson
  track="Write good integration tests"
  step="Lesson 7 of 7"
  minutes={8}
  outcomes={[
    'Decide whether a piece belongs to the host, to a test, or to neither',
    'Recognize a test that does work the host should own',
    'Name the cost of a host piece that only one test needs',
  ]}
  needs={[
    <>Lesson 3, <Link to="/learn/good-tests/add-and-remove-an-integration">Add and remove an integration</Link></>,
    'Nothing installed. The archives are on this site',
  ]}
/>

## The problem

Teams add pieces to the host whenever a test needs one. It works until the suite is slow, the run starts servers no test uses, and a test fails because a run hook seeded data for a different test.

Everything in the host runs for the whole run and is shared by every test. This lesson gives you two questions to decide where a piece goes.

## Do it

### 1. Ask two questions

For any piece, ask who shares it and how long it lives. The answers pick the placement:

| Who shares it | How long it lives | Placement | The sample's piece | Who releases it |
| --- | --- | --- | --- | --- |
| The whole run | Until the run ends | the host | the store, the broker, the loopback listener, the report sinks | the host, at the end of the run |
| More than one test | For the test | the test context | the tenant, the project, the sign-in, the attachments | teardown, in reverse order |
| One test | For the test | the test context | a fake, a stub, a single scenario's fixture | the test or its attribute, in teardown |
| One test | For the run | your own extension | a WireMock server only one journey needs, registered with `PerRun()` | the host, at the end of the run |
| Nobody yet | Not decided | neither | a piece of a system you do not control, until a test needs a door into it | nothing yet |

Run lifetime and shared by more than one test means the host. Test lifetime means the test context, whoever shares it.

The last row is the one teams skip. A system you do not control does not belong in the host either. Stub it with a fake the test or the run owns, and say which one it is.

### 2. Spot the three traps

**Doing the host's work in a test.** A raw client, a container started in a test body, or a sleep is work the run cannot manage and the trace cannot see. The environment drill in the [failure tour](/learn/understand-failures/a-failure-tour) is the evidence: two seconds of test execution, no request, and only the failure recorded where the operation should be. If the test needs an address, take it from the composition.

**A host piece that one test needs.** The host starts it for every run, even the runs that never touch it. WireMock's registration draws the line: a fake is per test by default, and `PerRun()` marks the one the suite shares. Reach for the run only when more than one test needs the same instance.

**A run hook that seeds data.** Seeding data so tests can read it by a fixed id breaks the state answer from lesson 5. Tests then depend on order, and a failure looks random.

### 3. See the third trap in a trace

Open [l0-state-drill.prototrace](pathname:///lessons/l0-state-drill.prototrace) and [l0-state-fix.prototrace](pathname:///lessons/l0-state-fix.prototrace) in the [viewer](https://trace.prototest.dev). The drill reads an id nobody created and fails. The fix creates what it reads. A seeding hook would put the suite back in the drill's position, with ids that depend on what the hook happened to seed.

## What happened

You sorted pieces by lifetime and sharing, not by size. The host holds what the whole run shares. Everything else stays in the test, where the trace sees it and teardown removes it.

If none of the placements fits, the answer is usually an extension rather than a workaround. A hook, an attribute, a client or an integration of your own plugs into the same lifecycle.

## Check yourself

<Checkpoint
  question="A team adds a run hook that seeds five projects so tests can read them by id. Which problem from the failure tour does that bring back, and what is the fix?"
  verify={<>Compare <a href="pathname:///lessons/l0-state-drill.prototrace">l0-state-drill.prototrace</a> with <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a> in the <a href="https://trace.prototest.dev">viewer</a>.</>}>

It brings back the state problem. Every test now reads data it did not create, and the ids depend on what the hook happened to seed. The fix is the one from lesson 5: the test creates what it reads, under a name built from its own test id, and teardown removes it.

</Checkpoint>

## Remember

- Run lifetime and sharing decide what goes in the host. Everything else stays in the test.
- A test that starts its own infrastructure is invisible to the trace and unmanaged at teardown.
- Seeding shared data in a run hook trades one setup line for failures that depend on run order.

## Go deeper

- [Recipes](/docs/recipes/overview): common journeys with the composed client, from REST to GraphQL to a workbook.
- [Extending ProtoTest](/docs/advanced/extending): when the right answer is your own hook, attribute or integration.
- [The foundation](/docs/foundation/overview): the lifecycle those extensions plug into.
