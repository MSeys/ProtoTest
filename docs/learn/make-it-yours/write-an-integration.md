---
id: write-an-integration
title: Write an integration
sidebar_label: Write an integration
sidebar_position: 3
description: "Read the extension-point map, follow the sample's custom client and hook, and make your own kind show up in the trace."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Write an integration

Everything a package does, your own code can do: a client the context resolves, a hook around the test, entries in the trace. The sample's correlation layer is a real integration of about fifty lines, and it is the model for this lesson.

<LearnShell
  level="Level 6, lesson 3"
  minutes="About 10 minutes"
  outcome={[
    'Pick the extension point your feature needs from the map.',
    'Follow a custom client from its initializer to its context call.',
    'Write your own kind, name and source into the trace correctly.',
  ]}
  before={[
    <>Provisioners and page objects (<Link to="/learn/make-it-yours/provisioners-and-page-objects">lesson 2</Link>).</>,
    'The sample cloned and open in an editor.',
  ]}
  situation={
    <>
      <p>A company client, a fixture loader, a file assertion: each is an integration, and each should feel native. Native means the test resolves it from the context, the run records it, and the failure message names it.</p>
      <p>The sample's scenario layer does exactly that: a custom client, an initializer, a hook, an attachment and two trace events. It is small enough to read in one sitting.</p>
    </>
  }
  checkpoint={{
    question:
      'A helper starts an operation with StartOperation and disposes it without calling Succeed, Fail or Complete. What does the trace record for that operation?',
    verify: (
      <>
        Read the adding-to-the-trace section of the extending page, then check the operations in any trace. An operation that was never completed shows its own outcome.
      </>
    ),
    reveal: (
      <>
        Disposing an operation without completing it records <code>Unknown</code>. The operation completes only once, so the code path that judges the work has to call <code>Succeed()</code>, <code>Fail(exception)</code> or <code>Complete(outcome)</code> before the using block ends.
      </>
    ),
  }}
  learned={[
    'There is one extension point per thing you want to add: attribute, hook, client, provisioner, sink or trace entry.',
    'A custom client is an initializer the host resolves and a context call that returns it.',
    'Kinds are dotted and lowercase, names are for humans, and the source is your package.',
  ]}
  next={[
    {
      label: 'Swap a dependency for one test',
      to: '/learn/make-it-yours/swap-a-dependency-for-one-test',
      note: 'Replace a service in the application for one test, and read the dedicated server it builds.',
    },
    {
      label: 'Extending ProtoTest',
      to: '/docs/advanced/extending',
      note: 'The full extension-point contract, including runners and sinks.',
    },
  ]}>

## What you want, and what to use

The extending page keeps this map. Read it as a menu, not a course:

| You want to | Use |
| --- | --- |
| package setup for some tests | a `ProtoAttribute` |
| run code around every test or the whole run | a hook |
| give tests a new client | a client initializer plus an extension method |
| create data in your system | a data provisioner |
| report on what tests did | observations and a collector |
| write reports somewhere | a sink |
| show up in the trace | the trace writer |

The sample's scenario layer uses three of them at once: a client, its initializer, and a hook that writes the trace and the attachment.

## The custom client

The probe is a plain class with a list of milestones. The initializer is what registers it with the context:

<AnnotatedCode
  filename="NorthstarScenario.cs"
  code={`public sealed class ScenarioProbeInitializer : IProtoClientInitializer<ScenarioProbe>
{
    public string Name => "ScenarioProbe";

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
    {
        context.RegisterClient(new ScenarioProbe(), Name);
        return Task.FromResult(true);
    }
}`}
  callouts={[
    {line: 1, title: 'One initializer per client type', note: 'The interface is generic over the client, so the host knows what it is creating.'},
    {line: 3, title: 'The name is the handle', note: 'A test resolves the client by this name. Returning false means not me: the next initializer in the group is tried, and a group with no winner fails setup with a named message.'},
    {line: 7, title: 'Register a live instance', note: 'The instance belongs to the run. Registering it here is what makes context.Client resolve instead of throwing.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarScenario.cs</code>.</>}
/>

Two registrations wire it up, one for the initializer and one for the hook:

```csharp
public static IProtoHostBuilder AddNorthstarTestSupport(this IProtoHostBuilder builder)
{
    builder.ConfigureServices(services => services.AddSingleton<IProtoClientInitializer, ScenarioProbeInitializer>());
    return builder.AddTestHook<NorthstarScenarioHook>();
}
```

The extension method is how the sample's `Setup.cs` stays readable: the integration owns its registrations, and the composition mentions one line.

## The trace entry

The hook writes the correlation and the milestone trail. This is the event that opens a scenario:

<AnnotatedCode
  filename="NorthstarScenario.cs"
  code={`context.Trace.WriteEvent(
    "northstar.scenario.begin",
    "Begin correlated Northstar scenario",
    "Northstar.ProtoTest",
    outcome: ProtoTraceOutcome.Succeeded,
    attributes: new Dictionary<string, string?>
    {
        ["northstar.correlation_id"] = scenario.CorrelationId
    });`}
  callouts={[
    {line: 2, title: 'Kind: dotted, lowercase', note: 'The viewer groups by the prefix before the first dot, so northstar events get their own category without a viewer release.'},
    {line: 3, title: 'Name: for humans', note: 'The built-ins use AREA, verb, subject. Keep the same shape so a reader can scan the execution layer.'},
    {line: 4, title: 'Source: your package', note: 'The source names what wrote the entry, which is how a reader tells framework entries from yours.'},
  ]}
  foot={<>The same two calls bracket the scenario: <code>northstar.scenario.begin</code> and <code>northstar.scenario.end</code>, with a duration attribute on the closing one.</>}
/>

The conventions are short and worth following:

- Attributes are strings. Keep them small, and never put a secret in one.
- Nesting is automatic. An operation started inside another becomes its child.
- An operation completes once. Completing it a second time is ignored, and disposing it uncompleted records `Unknown`.

## Your turn: one milestone

The probe is public, so a test can mark its own step. Add this to the first journey from lesson 1, under the `[RunNote]` you already added; if you removed that file, [Write your first test](/learn/one-test-one-journey/write-your-first-test) recreates it, and [Write your own attribute](/learn/make-it-yours/attributes) adds the note.

```csharp
Proto.Context.Client<ScenarioProbe>("ScenarioProbe").Mark("first-milestone");
```

Run the filter and open the trace. The teardown publishes the scenario summary as an attachment. Open it and read the milestones:

```json
{"CorrelationId":"scenario-416387000001-6406e159a16243e0beb564c28105893f","TestName":"Northstar.ProtoTest.ProjectsJourney.CreatingAProjectReturnsIt","DurationMs":349.127,"Milestones":["scenario-started","scenario-completed"]}
```

The committed archive for the first journey, <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, has the same shape, with the sample's own two milestones. Beside it, the trace holds:

| Entry | Reading |
| --- | --- |
| `Initialize · ScenarioProbe (ScenarioProbe)`, 0.1 ms | the initializer registered the custom client during setup |
| `Before · NorthstarScenarioHook`, 6.2 ms | the hook ran before the test and wrote the opening event |
| `Publish · <test id>-scenario-summary.json`, 0.6 ms | the hook attached the milestone trail at teardown |

Your milestone lands in the same attachment, because the client, the hook and the attachment are one integration. That is the whole point: a feature you write behaves like a feature that shipped.

</LearnShell>
