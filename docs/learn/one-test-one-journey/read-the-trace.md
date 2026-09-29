---
id: read-the-trace
title: Read the trace
sidebar_label: Read the trace
sidebar_position: 3
description: "Walk a trace layer by layer, find the same layers in your own run, and let a failure message name the fix."
---

import LearnShell from '@site/src/components/LearnShell';
import TraceAnatomy from '@site/src/components/TraceAnatomy';
import Link from '@docusaurus/Link';

# Read the trace

Your first journey passed. The trace holds what it actually did: what the run composed, what setup provisioned, what the test called, what each check read, and what teardown released.

<LearnShell
  level="Level 1, lesson 3"
  minutes="About 10 minutes"
  outcome={[
    'Walk a test trace layer by layer: run, setup, execution, teardown.',
    'Find the same layers in a trace your own test wrote.',
    'Read a failure message that names the property and both values.',
  ]}
  before={[
    <>Write your first test (<Link to="/learn/one-test-one-journey/write-your-first-test">lesson 2</Link>). Keep <code>MyFirstJourney.cs</code> if you want your own trace to compare, and for the Level 6 exercises that reuse it.</>,
    'Nothing installed if you read the archive; running needs the sample from lesson 1.',
  ]}
  situation={
    <>
      <p>A passing test is the easy case. The hard case is the same test failing at 3 a.m., where the only witness is the trace. This lesson reads one test slowly, then finds the same shape in the trace your own test wrote.</p>
      <p>The walk below reads the clock journey from Level 0, because it holds one of each layer. Your first journey has the same shape with fewer entries.</p>
    </>
  }
  checkpoint={{
    question:
      'Change the shape assertion to expect a different name, for example name = "someone-else". Before you run it, predict what the failure message will name.',
    verify: (
      <>
        Run the filtered test and read the failure message the runner prints, then open the trace and find the same failed check inside it. Restore the assertion and run again.
      </>
    ),
    reveal: (
      <>
        The message names the property and both values: <code>[$.name]: Values did not match. (Expected: "someone-else", Actual: "...")</code>. The actual value is the name the test sent in the request, so the message points at the value to fix. The shape check reads the whole response, not one field, and reports every property that differs.
      </>
    ),
  }}
  learned={[
    'A trace answers what ran, where it ran, and what each check read.',
    'Setup and teardown are part of the story, not noise around it.',
    'A failing shape check names the property and both values, so the fix is visible in the message.',
  ]}
  next={[
    {
      label: 'Level 2: Compose, don\'t glue',
      to: '/learn/compose-dont-glue/capabilities-and-the-host',
      note: 'What a capability is, who serves it, and how a run adds or drops one.',
    },
    {
      label: 'The trace reference',
      to: '/docs/observability/prototrace',
      note: 'Every trace kind, what it records, and what the trace cannot see.',
    },
  ]}>

## One test, layer by layer

<TraceAnatomy />

## The same layers in your own trace

Your first journey leaves a smaller trace with the same four layers. From `l1-first-journey.prototrace`:

- **Run**: the capabilities the composition declared, the in-process application, and the loopback instance the browser journey follows.
- **Setup**: each client initializes once (`Initialize · Rest:Northstar`, `GraphQL:Northstar`, and the rest), the SQL connection opens, `NorthstarTenant` provisions the tenant with `data.create · ProvisionTenantRequest`, and `SignedInAs` sets the member identity.
- **Execution**: `POST /api/v1/projects` leaves the request, the response and the expected shape as artifacts; the auth handlers apply; the application reports its own `project.create`; and the two checks record what they read.
- **Teardown**: four attachments publish, `data.cleanup · TenantResponse` removes the tenant, and each owned resource releases in order.

When your test passes, that list is the proof of what it did. When it fails, the same list tells you which step to read first.

## When a check fails

The checks are the part to read first, because each one records what it compared. A status check names the status it expected and the one it received. A shape check names the JSON path and both values:

```
[$.status]: Values did not match. (Expected: "past_due", Actual: "active")
```

That is the clock drill from Level 0. The message says which property, what the test expected and what the application sent. Read the call the check judged and its inputs. The fix is one value or one clock move.

The two places that hold those values are the runner output and the trace. The trace records the failed `assert.json.shape` entry and, beside it, the expected shape as an artifact, so the shape the test asked for is in the file too. The reports are the run's verdict: `report.html` marks the test failed and `report.json` carries the coverage and traffic rows. Neither one carries the expected and actual values, so a report is the place to confirm that a test failed, not the place to find out why.

</LearnShell>
