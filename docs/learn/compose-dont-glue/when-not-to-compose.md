---
id: when-not-to-compose
title: When not to compose
sidebar_label: When not to compose
sidebar_position: 5
description: "Decide what belongs in the run, what belongs to a test, and what belongs to neither."
---

import LearnShell from '@site/src/components/LearnShell';
import Link from '@docusaurus/Link';

# When not to compose

Composition has a cost: everything in the host runs for the whole run and is shared by every test. This lesson is about the line between composing and gluing.

<LearnShell
  level="Level 2, lesson 5"
  minutes="About 8 minutes"
  outcome={[
    'Decide whether a piece belongs to the run, to a test, or to neither.',
    'Recognize a test that glues what the composition should own.',
    'Name the cost of a piece that only one test needs.',
  ]}
  before={[
    <>Add and remove an integration (<Link to="/learn/compose-dont-glue/add-and-remove-an-integration">lesson 3</Link>).</>,
    'Nothing installed. The archives are on this site.',
  ]}
  situation={
    <>
      <p>Teams add pieces to the host whenever a test needs them. It works until the suite is slow, the run starts servers no test uses, and a test fails because a run hook seeded data for a different test.</p>
      <p>The line is drawn by lifetime and by sharing, not by size. This lesson walks the cases from the sample.</p>
    </>
  }
  checkpoint={{
    question:
      'A team adds a run hook that seeds five projects so tests can read them by id. Which question from Level 0 does that break, and what does the fix look like?',
    verify: (
      <>
        Compare <a href="pathname:///lessons/l0-state-drill.prototrace">l0-state-drill.prototrace</a> with{' '}
        <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a> in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>.
      </>
    ),
    reveal: (
      <>
        It breaks state. Every test now reads data it did not create, and the ids depend on what the hook happened to seed. The fix is the same as the state fix: the test creates what it reads, under a name built from its own test id, and teardown removes it.
      </>
    ),
  }}
  learned={[
    'Run lifetime and sharing decide what goes in the host; everything else stays in the test.',
    'A test that starts its own infrastructure is invisible to the trace and unmanaged at teardown.',
    'Seeding shared data in a run hook trades one setup line for failures that depend on run order.',
  ]}
  next={[
    {
      label: 'Recipes',
      to: '/docs/recipes/overview',
      note: 'Common journeys with the composed client, from REST to GraphQL to a workbook.',
    },
    {
      label: 'Extending ProtoTest',
      to: '/docs/advanced/extending',
      note: 'When the right answer is your own hook, attribute or integration.',
    },
  ]}>

## Where a piece belongs

Ask two things about a piece before adding it to the host: who shares it, and how long it lives. The two answers pick the placement.

| Who shares it | How long it lives | Placement | The sample's piece | Who releases it |
| --- | --- | --- | --- | --- |
| The whole run | Until the run ends | the host | the store, the broker, the loopback listener, the report sinks | the host, at the end of the run |
| More than one test | For the test | the execution context | the tenant, the project, the sign-in, the attachments | teardown, in reverse order |
| One test | For the test | the execution context | a fake, a stub, a single scenario's fixture | the test or its attribute, in teardown |
| One test | For the run | your own extension | a WireMock server only one journey needs, registered with `PerRun()` | the host, at the end of the run |
| Nobody yet | Not decided | neither | a piece of a system you do not control, until a test needs a door into it | nothing yet |

The last row is the one teams skip. A system you do not control does not belong in the host either. Stub it with a fake the test or the run owns, and say which one it is.

Run lifetime and shared by more than one test means the host. Test lifetime means the execution context, whatever shares it.

## Traps

**Glue in the test.** A raw client, a container started in a test body, or a sleep is work the run cannot manage and the trace cannot see. The environment drill from Level 0 is the evidence: two seconds of test execution, no request, and only the failure recorded where the operation should be. If the test needs an address, take it from the composition.

**A fixture in the host that one test needs.** The host starts it for every run, even the runs that never touch it. WireMock's registration draws the line: a fake is per test by default, and `PerRun()` marks the one the suite shares. Reach for the run only when more than one test needs the same instance.

**The run as a script.** Seeding data in a run hook so tests can read it by a fixed id breaks the state answer. The tests then depend on order, and a failure looks random. Create data in the test that reads it, and let teardown remove it.

## When your own piece is the answer

If none of the three placements fits, the answer is usually an extension, not a workaround. The [extending page](/docs/advanced/extending) covers hooks, attributes, clients and integrations, and the [foundation](/docs/foundation/overview) explains the lifecycle those extensions plug into.

</LearnShell>
