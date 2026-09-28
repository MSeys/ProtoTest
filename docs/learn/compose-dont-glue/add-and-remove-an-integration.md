---
id: add-and-remove-an-integration
title: Add and remove an integration
sidebar_label: Add and remove an integration
sidebar_position: 2
description: "Watch a capability-gated journey skip, add the broker that serves it, and remove it again."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Add and remove an integration

A capability the run cannot serve is absent. A test that needs it does not fail halfway; it skips with a reason. This lesson watches that happen, adds the missing piece, and takes it away again.

<LearnShell
  level="Level 2, lesson 2"
  minutes="About 10 minutes"
  outcome={[
    'Run a capability-gated journey and read its skip.',
    'Add the broker to the composition and watch the same journey pass.',
    'Remove it again without touching the test.',
  ]}
  before={[
    <>Capabilities and the host (<Link to="/learn/compose-dont-glue/capabilities-and-the-host">lesson 1</Link>).</>,
    'For the container step: Docker running. The skip and its archive need nothing installed.',
  ]}
  situation={
    <>
      <p>The broker journey pays an invoice and then waits for the application's <code>invoice.paid</code> event on its own tap. In an ordinary run it skips: no broker is configured.</p>
      <p>The journey is not broken. The run simply cannot serve the capability it declares, and the skip says so before the test starts.</p>
    </>
  }
  checkpoint={{
    question:
      'The skipped run\'s trace holds three entries, all of them releases, and no test execution. Why not?',
    verify: (
      <>
        Download <a href="pathname:///lessons/l2-broker-skip.prototrace">l2-broker-skip.prototrace</a>, open it in the{' '}
        <a href="https://trace.prototest.dev">viewer</a>, and compare it with a passing journey's archive, which opens at test setup.
      </>
    ),
    reveal: (
      <>
        The capability gate runs before the test lifecycle starts, so the test never began and there is nothing to record. The trace is honest about that: a run with no tests in it. The reason lives in the runner output, and the composition registers it with <code>AddCapabilityReason</code>.
      </>
    ),
  }}
  learned={[
    'A run serves a capability only when something in it can.',
    'A gated test skips before its lifecycle starts, so its trace holds no test.',
    'Adding or removing an integration is a composition change, not a test change.',
  ]}
  next={[
    {
      label: 'When not to compose',
      to: '/learn/compose-dont-glue/when-not-to-compose',
      note: 'Decide what belongs in the run, what belongs to a test, and what belongs to neither.',
    },
    {
      label: 'Messaging',
      to: '/docs/integrations/messaging',
      note: 'Publish and await messages on the in-memory broker or RabbitMQ.',
    },
  ]}>

## The journey and its gate

`BrokerJourney` declares the capability it needs on the class:

```csharp
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
[RequiresCapability(ProtoCapabilityKinds.Broker)]
public sealed class BrokerJourney
```

Run it alone and the runner reports a skip:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~BrokerJourney"
```

The reason is the one the composition registered: "No broker is configured; set ProtoTest:Messaging:Broker=container."

## The composition decides

This is `ConfigureMessaging` from `Setup.cs`:

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
    {line: 5, title: 'One switch, two compositions', note: 'The run decides per configuration whether it has a broker, and the test never asks.'},
    {line: 7, title: 'Messaging with attachments', note: 'Both branches compose messaging; only the adapter differs.'},
    {line: 9, title: 'The adapter is the capability', note: 'UseRabbitMq declares the Broker capability and registers the broker as a run resource. Without an adapter, the in-memory default serves the API but declares no Broker capability.'},
    {line: 11, title: 'Declare and tap before the first await', note: 'The event is declared and tapped during setup, so the test cannot miss a publish.'},
    {line: 15, title: 'No adapter, no capability', note: 'The in-memory broker keeps the API usable, and a test that needs a real broker skips instead of passing against the double.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The reason in the skip comes from <code>AddCapabilityReason(ProtoCapabilityKinds.Broker, "No broker is configured; ...")</code> in the same file.</>}
/>

## What the skipped run recorded

Open `l2-broker-skip.prototrace` and the run has no tests in it. Its only operations are the run's own releases: the broker resource, the readiness probe and the loopback application. No setup, no execution, no checks.

That is worth knowing before you debug a skip: there is no test trace to read, because the test never started. The runner output is where the reason lives, and the reason is real text from the composition, not a generic "test skipped".

## Add the broker

Give the run a broker it owns. The container needs Docker:

```powershell
$env:ProtoTest__Messaging__Broker = "container"
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~BrokerJourney"
```

The run starts a RabbitMQ container, the journey pays the invoice over REST, and the test awaits `invoice.paid` on the tap. It passes, and the trace now holds the request, the messaging publish and await, and the container in the run layer. The test file did not change.

## Remove it

Start a new terminal, or clear the variable, and run the journey again:

```powershell
Remove-Item Env:ProtoTest__Messaging__Broker
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~BrokerJourney"
```

The skip is back, with the same reason. The composition reads the setting once per run, so adding or removing an integration is a run decision.

</LearnShell>
