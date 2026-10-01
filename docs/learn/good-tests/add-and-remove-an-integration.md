---
id: add-and-remove-an-integration
title: Add and remove an integration
sidebar_label: Add and remove an integration
sidebar_position: 3
description: "Watch a test skip because the run lacks a capability, add the broker that serves it, and remove it again without touching the test."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Add and remove an integration

<Lesson
  track="Write good integration tests"
  step="Lesson 3 of 7"
  minutes={10}
  outcomes={[
    'Run a test that needs a capability the run lacks, and read its skip',
    'Add the broker to the composition and watch the same test pass',
    'Remove it again without touching the test',
  ]}
  needs={[
    <>Lesson 1, <Link to="/learn/good-tests/capabilities-and-the-host">Read what your run provides</Link></>,
    'For the container step: Docker running. The skip and its archive need nothing installed',
  ]}
/>

## The problem

The sample's broker journey pays an invoice and then waits for the application's `invoice.paid` event. In an ordinary run it skips. The test is not broken. The run has no broker, so it cannot serve the capability the test asks for.

You will see the skip, add the broker, and remove it again. The test file never changes.

## Do it

### 1. Run the test without a broker

`BrokerJourney` declares what it needs on the class:

```csharp
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
[RequiresCapability(ProtoCapabilityKinds.Broker)]
public sealed class BrokerJourney
```

Run it alone:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~BrokerJourney"
```

The runner reports a skip with the reason the composition registered: "No broker is configured; set ProtoTest:Messaging:Broker=container."

### 2. Read the skip's trace

Open [l2-broker-skip.prototrace](pathname:///lessons/l2-broker-skip.prototrace) in the [viewer](https://trace.prototest.dev). It has no test in it. Its only operations are the run's own releases: the broker resource, the readiness probe and the loopback application. No setup, no execution, no checks.

A skipped test leaves no trace of its own, because it never started. The reason is in the runner output.

### 3. Add the broker

The composition decides whether the run has a broker. This is `ConfigureMessaging` from `Setup.cs`:

<AnnotatedCode
  filename="Setup.cs"
  code={`private static void ConfigureMessaging(IProtoHostBuilder builder, NorthstarRun run)
{
    // Registered last so the application exists before messaging binds its taps. The tap is declared
    // in code so it is bound during setup, not at the first await, and cannot miss the publish.
    if (run.UsesMessaging)
    {
        builder.AddMessaging(messaging => messaging
            .CaptureAttachments()
            .UseRabbitMq()
            .Declare("invoice.paid")
            .Tap("invoice.paid"));
    }
    else
    {
        builder.AddMessaging(messaging => messaging.CaptureAttachments());
    }
}`}
  callouts={[
    {line: 5, title: 'One switch', note: 'The run decides per configuration whether it has a broker. The test never asks.'},
    {line: 9, title: 'The adapter is the capability', note: 'UseRabbitMq declares the Broker capability and registers the broker as a run resource.'},
    {line: 15, title: 'No adapter, no capability', note: 'The in-memory default keeps the API usable but declares no Broker capability, so a test that needs one skips instead of passing against a double.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The skip reason comes from <code>AddCapabilityReason(ProtoCapabilityKinds.Broker, "No broker is configured; ...")</code> in the same file.</>}
/>

Turn the switch on with a setting. The container needs Docker:

```powershell
$env:ProtoTest__Messaging__Broker = "container"
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~BrokerJourney"
```

The run starts a RabbitMQ container, the journey pays the invoice over REST, and the test awaits `invoice.paid`. It passes. The trace now holds the request, the messaging publish and await, and the container in the run layer.

### 4. Remove it

Clear the variable and run again. `-ErrorAction SilentlyContinue` keeps the command working in a terminal that never set it:

```powershell
Remove-Item Env:ProtoTest__Messaging__Broker -ErrorAction SilentlyContinue
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~BrokerJourney"
```

The skip is back, with the same reason.

## What happened

One setting changed the composition, and the composition changed which capabilities the run declared:

| | No broker configured | `ProtoTest__Messaging__Broker=container` |
| --- | --- | --- |
| The run's messaging | the in-memory default, no adapter | `UseRabbitMq()`, which declares the Broker capability |
| `BrokerJourney` | skipped, with the registered reason | runs: it pays the invoice and awaits `invoice.paid` |
| The archive | three releases and no test | the request, the publish, the await and the container |
| The test file | unchanged | unchanged |

The capability gate runs before a test's lifecycle starts. That is why the skipped run has no test in its trace. Adding or removing an integration is a run decision, made once when the host is composed.

## Check yourself

<Checkpoint
  question="The skipped run's trace holds three entries, all releases, and no test execution. Why?"
  verify={<>Compare <a href="pathname:///lessons/l2-broker-skip.prototrace">l2-broker-skip.prototrace</a> with a passing journey's archive, which opens at test setup.</>}>

The capability gate runs before the test lifecycle starts, so the test never began and there is nothing to record. The trace holds a run with no tests in it. The reason is in the runner output, and the composition registers it with `AddCapabilityReason`.

</Checkpoint>

## Remember

- The run leaves out a capability it cannot serve, and a test that needs it skips.
- A skipped test has no trace of its own. Read the runner output for the reason.
- Adding or removing an integration changes the composition, not the test.

## Go deeper

- [Messaging](/docs/integrations/messaging): publish and await messages on the in-memory broker or RabbitMQ.
- Next: [Sign in as a test user](/learn/good-tests/sign-in-as-a-test-user).
