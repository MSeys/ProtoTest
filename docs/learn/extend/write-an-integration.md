---
id: write-an-integration
title: Write an integration
sidebar_label: Write an integration
sidebar_position: 3
description: "Add your own client, hook and trace entries so a system ProtoTest does not know behaves like a built-in one."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# Write an integration

<Lesson
  track="Extend ProtoTest"
  step="Lesson 3 of 5"
  minutes={10}
  outcomes={[
    'Choose the extension point your feature needs',
    'Follow a custom client from its initializer to its call in a test',
    'Write your own entries into the trace',
  ]}
  needs={[
    <>The previous lesson, <a href="/learn/extend/provisioners-and-page-objects">Provisioners and page objects</a></>,
    'The sample cloned and open in an editor',
  ]}
/>

## The problem

Your system has something ProtoTest does not know: a company client library, a fixture loader, a file format. You want tests to use it as naturally as `Proto.Context.Rest()`. That means the test gets it from the context, the run records what it did, and a failure message names it.

An integration is a package that adds this kind of capability. The sample's scenario layer is a real one of about fifty lines: a custom client, a hook around each test, an attachment and two trace events.

## Do it

### 1. Pick the extension point

Read this table as a menu. The scenario layer uses three rows: a client, its initializer, and a hook that writes the trace and the attachment.

| You want to | Use |
| --- | --- |
| package setup for some tests | a `ProtoAttribute` |
| run code around every test or the whole run | a hook |
| give tests a new client | a client initializer plus an extension method |
| create data in your system | a data provisioner |
| report on what tests did | observations and a collector |
| write reports somewhere | a sink |
| show up in the trace | the trace writer |

### 2. Register the custom client

The probe is a plain class that keeps a list of milestones. The initializer registers it with the test context:

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
    {line: 3, title: 'The name is the handle', note: 'A test resolves the client by this name. Returning false lets the next initializer try.'},
    {line: 7, title: 'Register a live instance', note: 'This is what makes the client resolve from the context.'},
  ]}
  foot={<>From <code>samples/Northstar.ProtoTest/NorthstarScenario.cs</code>.</>}
/>

Two registrations wire it up, one for the initializer and one for the hook. An extension method keeps them in one place, so the sample's `Setup.cs` mentions a single line:

```csharp
public static IProtoHostBuilder AddNorthstarTestSupport(this IProtoHostBuilder builder)
{
    builder.ConfigureServices(services => services.AddSingleton<IProtoClientInitializer, ScenarioProbeInitializer>());
    return builder.AddTestHook<NorthstarScenarioHook>();
}
```

### 3. Write to the trace

The hook opens and closes each scenario with a trace event. This is the opening one:

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
    {line: 2, title: 'Kind: dotted, lowercase', note: 'The viewer labels a kind it does not know by its first segment: Northstar.'},
    {line: 3, title: 'Name: for humans', note: 'A verb and a subject, like the built-in entries.'},
    {line: 4, title: 'Source: your package', note: 'It separates your entries from the framework\'s.'},
  ]}
  foot={<>The same two calls bracket the scenario: <code>northstar.scenario.begin</code> and <code>northstar.scenario.end</code>, with a duration attribute on the closing one.</>}
/>

Three rules keep your entries tidy:

- Attributes are strings. Keep them small, and never put a secret in one.
- Nesting is automatic. An operation started inside another becomes its child.
- Complete an operation once. A second completion has no effect, and disposing it without completion records `Unknown`.

### 4. Use the client in a test

Add this line to the first journey from [Write your own attribute](/learn/extend/attributes), under the `[RunNote]` you added there:

```csharp
Proto.Context.Client<ScenarioProbe>("ScenarioProbe").Mark("first-milestone");
```

Run the filtered test. At teardown the hook attaches the scenario summary. A real one from the sample:

```json
{"CorrelationId":"scenario-416387000001-6406e159a16243e0beb564c28105893f","TestName":"Northstar.ProtoTest.ProjectsJourney.CreatingAProjectReturnsIt","DurationMs":349.127,"Milestones":["scenario-started","scenario-completed"]}
```

Your own run lists `first-milestone` beside the sample's two.

## What happened

You did not change the framework. The host resolved your initializer at setup, so `Client<ScenarioProbe>` found the client. The hook ran around the test, wrote the events and attached the milestone trail.

The committed trace of the first journey, <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a>, shows the sample's own entries:

| Entry | Reading |
| --- | --- |
| `Initialize · ScenarioProbe (ScenarioProbe)`, 0.1 ms | the initializer registered the client in setup |
| `Before · NorthstarScenarioHook`, 5.9 ms | the hook wrote the opening event |
| `Publish · <test id>-scenario-summary.json`, 0.5 ms | the hook attached the milestones at teardown |

A feature you write behaves like a feature that shipped.

## Check yourself

<Checkpoint
  question="The first journey runs with the scenario hook registered. Which entry in its trace proves the custom client was registered, and where does your own milestone land?"
  verify={<>Open <a href="pathname:///lessons/l1-first-journey.prototrace">l1-first-journey.prototrace</a> in the <a href="https://trace.prototest.dev">viewer</a> and read the setup and teardown phases, or read the table above.</>}>

<code>Initialize · ScenarioProbe (ScenarioProbe)</code> in the setup phase. Your milestone lands beside the sample's two, in the scenario summary the hook attaches at teardown.

</Checkpoint>

## Remember

- There is one extension point per thing you add: attribute, hook, client, provisioner, sink or trace entry.
- A custom client is an initializer the host resolves, plus a context call that returns it.
- Kinds are dotted and lowercase, names are for humans, and the source is your package.

Next: [swap a dependency for one test](/learn/extend/swap-a-dependency-for-one-test).

## Go deeper

- [Extending ProtoTest](/docs/advanced/extending): the full extension-point contract, including runners and sinks.
