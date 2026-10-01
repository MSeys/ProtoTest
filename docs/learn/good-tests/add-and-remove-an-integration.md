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
    'The sample cloned and the .NET SDK installed to run the commands. Reading the saved archive needs neither.',
    'Docker running for the container step',
  ]}
/>

## The problem

The sample's broker journey pays an invoice and waits for the application's `invoice.paid` event. With the default settings, it skips because the run has no real broker. The test declares that requirement through the `Broker` capability.

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

Use the sample's default settings, with no broker mode, RabbitMQ connection string or external application URL configured. From the repository root, run it alone:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~BrokerJourney"
```

The runner reports the reason registered by the setup class: "No broker is configured; set ProtoTest:Messaging:Broker=container."

### 2. Read the skip's trace

Open [l2-broker-skip.prototrace](pathname:///lessons/l2-broker-skip.prototrace) in the [viewer](https://trace.prototest.dev). This recording has no test in it. Its three operations release run-owned resources: the messaging broker resource, readiness probe and loopback application. It contains no test setup, execution or checks.

The capability check skips the test before its lifecycle starts, so the reason is in the runner output.

### 3. Add the broker

The setup class chooses which messaging implementation the run uses. This is its `ConfigureMessaging` method:

<AnnotatedCode
  filename="Setup.cs"
  code={`private static void ConfigureMessaging(IProtoHostBuilder builder, NorthstarRun run)
{
    // Registered last so the application exists before messaging binds its taps. The tap is declared
    // in code so it is bound during setup, not at the first await, and does not miss a publish that
    // happens before the test awaits.
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
    {line: 6, title: 'One switch', note: 'The run decides per configuration whether it has a broker. The test never asks.'},
    {line: 10, title: 'Register the RabbitMQ adapter', note: 'The Broker capability requires a configured address or infrastructure that supplies one. Registration alone does not prove the broker is reachable.'},
    {line: 16, title: 'No adapter, no capability', note: 'The in-memory default keeps the API usable but declares no Broker capability, so a test that needs one skips instead of passing against a double.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/Setup.cs</code>. The skip reason comes from <code>AddCapabilityReason(ProtoCapabilityKinds.Broker, "No broker is configured; ...")</code> in the same file.</>}
/>

Turn on container mode with this setting. With no existing broker address configured, the run starts its own RabbitMQ container. Docker must be running:

```powershell
$env:ProtoTest__Messaging__Broker = "container"
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~BrokerJourney"
```

The journey should now pass: it pays the invoice over REST and awaits `invoice.paid`. Look for the REST request and messaging await in the test's trace, and container operations in the run layer.

`ConfigureInfrastructure` in the same setup class tries a configured address before starting a container. If you already supplied a RabbitMQ connection string, the run uses that broker instead.

At the end of the run, ProtoTest releases its broker client and any container it started. It does not stop an externally supplied broker.

### 4. Remove it

Clear the variable and run again. `-ErrorAction SilentlyContinue` keeps the command working in a terminal that never set it:

```powershell
Remove-Item Env:ProtoTest__Messaging__Broker -ErrorAction SilentlyContinue
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~BrokerJourney"
```

With no RabbitMQ connection string configured, the next run skips again with the same reason. Clearing the container setting alone does not disable an explicitly configured broker.

## What happened

With the default settings used above, the switch changes the adapter and the capability available to the test:

| | No broker configured | `ProtoTest__Messaging__Broker=container` |
| --- | --- | --- |
| The run's messaging | the in-memory default, no `Broker` capability | RabbitMQ adapter with a container-provided address and the `Broker` capability |
| `BrokerJourney` | skipped, with the registered reason | runs: it pays the invoice and awaits `invoice.paid` |
| The archive | the saved example has three releases and no test | the REST request, messaging await and container operations |
| The test file | unchanged | unchanged |

`AddMessaging` stays registered in both modes. Only the adapter changes. A new setting affects the next run, not a host already running.

## Check yourself

<Checkpoint
  question="The skipped run's trace holds three entries, all releases, and no test execution. Why?"
  verify={<>Compare <a href="pathname:///lessons/l2-broker-skip.prototrace">l2-broker-skip.prototrace</a> with a passing journey's archive, which opens at test setup.</>}>

The capability check stopped this test before setup, so it has no test lifecycle to record. The host still released its run-owned resources. The runner output carries the reason registered through `AddCapabilityReason`.

</Checkpoint>

## Remember

- A missing `Broker` capability skips this test before setup. A declared capability does not guarantee a healthy connection.
- Read the runner output for the skip reason. The saved trace shows the run's cleanup.
- Choose the adapter in host setup. Keep the test's requirement unchanged.

## Go deeper

- [Messaging](/docs/integrations/messaging): publish and await messages on the in-memory broker or RabbitMQ.
- Next: [Sign in as a test user](/learn/good-tests/sign-in-as-a-test-user).
