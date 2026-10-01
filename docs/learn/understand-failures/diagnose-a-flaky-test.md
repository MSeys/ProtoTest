---
id: diagnose-a-flaky-test
title: Diagnose a flaky test by comparing two runs
sidebar_label: Diagnose a flaky test
sidebar_position: 4
description: "Line up a passing and a failing run of the same test, find the first value that differs, and name what changed it."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# Diagnose a flaky test by comparing two runs

<Lesson
  track="Understand failures"
  step="Lesson 4 of 9"
  minutes={10}
  outcomes={[
    'Line up a passing and a failing run of the same test',
    'Find the first recorded value that differs',
    'Confirm a timing cause by making the failure reproducible',
  ]}
  needs={[
    <>The previous lesson, <Link to="/learn/understand-failures/the-trace-as-the-feedback-loop">the trace as a feedback loop</Link></>,
    'Nothing installed. The archives are on this site. Running the drill yourself needs the sample cloned.',
  ]}
/>

## The problem

A test passes on your machine and fails on the build agent. Run it again and it passes. Rerunning until green hides the problem, and one failure alone rarely explains it.

Compare a passing run with a failing run of the same test instead. Whatever is the same in both did not cause the failure. The first value that differs points at the cause.

## Do it

### 1. Get both outcomes

The sample's drill `WebhookJourney.OneReadRacesTheDispatcher` subscribes a webhook, creates a project, and reads the delivery list once:

```csharp
using var deliveries = await Proto.Context.Rest().GetAsync("/api/v1/webhook-deliveries");
deliveries
    .Should.HaveHttpStatus(HttpStatusCode.OK)
    .Should.MatchShape(new
    {
        items = new[]
        {
            new { eventType = WebhookEventTypes.ProjectCreated, status = WebhookDeliveryStatuses.Delivered },
        },
    });
```

It passes in most runs and fails in some: 4 of 25 on one machine. Both archives are kept on this site:

- [l4-flaky-pass.prototrace](pathname:///lessons/l4-flaky-pass.prototrace), a run that passed
- [l4-flaky-fail.prototrace](pathname:///lessons/l4-flaky-fail.prototrace), a run that failed, recorded with the setting from step 5

To record your own pair, run the drill ten times from the repository root. Drills skip unless `ProtoTest__Sample__Drills` is set:

```bash
for i in $(seq 10); do
  ProtoTest__Sample__Drills=true dotnet test samples/Northstar.ProtoTest \
    --filter "FullyQualifiedName~WebhookJourney.OneReadRacesTheDispatcher"
done
```

Each run writes a new archive under `samples/Northstar.ProtoTest/bin/Debug/net8.0/TestResults/`. Keep one that passed and one that failed.

### 2. Line up the operations

Open each archive in the [viewer](https://trace.prototest.dev), select the test, and open Execution. The two runs record the same operations in the same order:

| Operation | Passing run | Failing run |
| --- | --- | --- |
| `Create · ConfigureWebhookSinkRequest` | succeeded | succeeded |
| `REST · POST /api/v1/webhooks` | 201 Created | 201 Created |
| `REST · POST /api/v1/projects` | 201 Created | 201 Created |
| `REST · GET /api/v1/webhook-deliveries` | 200 OK | 200 OK |
| `Assert response shape` | succeeded | failed |

Every request got the same status. The durations differ by tens of milliseconds, as they do between any two runs. The failure is not in a step the test took.

### 3. Find the first value that differs

Open the response attachment of `GET /api/v1/webhook-deliveries` in each run:

| Field | Passing run | Failing run |
| --- | --- | --- |
| `status` | `delivered` | `pending` |
| `attempts` | `1` | `0` |
| `deliveredAtUtc` | set | `null` |

The failed check names the same field: `[$.items[0].status]: Values did not match. (Expected: "delivered", Actual: "pending")`.

`attempts` says more than `status`. In the failing run, the application had not tried to deliver yet. Nothing failed on the way to the sink. The read came first.

### 4. Name what changed the value

No operation in either trace sets `status`. Something outside the test does. In the sample, a background dispatcher sends queued deliveries every 100 ms on a real timer. The hypothesis: whether it runs before the read decides the outcome.

### 5. Confirm it by widening the race

A timing hypothesis is confirmed when you can make the slow side slower and the failure follows. The sample reads its dispatcher interval from `Northstar:WebhookDispatchInterval`. Set it to two seconds:

```bash
Northstar__WebhookDispatchInterval=00:00:02 ProtoTest__Sample__Drills=true dotnet test samples/Northstar.ProtoTest \
  --filter "FullyQualifiedName~WebhookJourney.OneReadRacesTheDispatcher"
```

The drill now fails every time: 10 of 10 runs. The flake has become a failure you can reproduce on demand.

Then run the fix, `WebhookJourney.CreatingAProjectDeliversItsWebhook`, with the same setting. It passes, because it polls until the delivery arrives instead of reading once. [Wait for a read that lags a write](/learn/reliable-tests/wait-for-a-lagging-read) explains that test.

## What happened

Two runs of the same test recorded the same operations and statuses. The first difference was a value no step of the test wrote, which pointed at the dispatcher's timer. Slowing that timer turned the flake into a steady failure, and the polling fix passed under the same setting.

## Check yourself

<Checkpoint
  question="In a third run, the GET answers 500 instead of 200. Is that the same flaky failure?"
  verify={<>Compare with the table in step 2: which row would differ first?</>}>

No. The first difference would be the status of the GET, not a field of its body. That points at the application failing to answer, which is a different cause to look for.

</Checkpoint>

## Remember

- To diagnose a flaky test, compare a passing and a failing run of the same test.
- What is the same in both runs did not cause the failure. Look for the first value that differs.
- When no step of the test changed that value, look for work outside the test, such as a timer, a queue or another test.
- Confirm a timing cause by making the slow side slower until the test fails every run.

## Go deeper

- [Wait for a read that lags a write](/learn/reliable-tests/wait-for-a-lagging-read): the fix for this drill.
- [Findings](/learn/understand-failures/findings): the next lesson, for runs that fail while every test passes.
