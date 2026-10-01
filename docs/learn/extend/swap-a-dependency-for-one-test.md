---
id: swap-a-dependency-for-one-test
title: Swap a dependency for one test
sidebar_label: Swap a dependency
sidebar_position: 5
description: "Replace one service in the application under test for a single test, read the dedicated server in the trace, and see when the framework refuses."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# Swap a dependency for one test

<Lesson
  track="Extend ProtoTest"
  step="Lesson 5 of 6"
  minutes={9}
  outcomes={[
    'Replace a service in the application for one test with Override',
    'Read the substitution and the dedicated server it builds in the trace',
    'Know when a substitution skips or throws, and why',
  ]}
  needs={[
    <>The previous lesson, <a href="/learn/extend/write-an-integration">Write an integration</a></>,
    'The sample cloned. The substitution run needs nothing else installed.',
  ]}
/>

## The problem

You want to test what happens when one dependency misbehaves, or returns a fixed value, and keep everything else real. The outbox tests of the OpenCSMS product do this: they replace the event publisher for one test with one that fails a few times, then delegates to the real publisher. Nothing else on that path is faked.

The seam for this is `Override`. It needs the application hosted in-process, because the replacement goes into the application's own service container.

## Do it

### 1. See the three forms

The fault injection lesson used the body form in one line:

```csharp
Proto.Context.Override<IEventPublisher>(publisher);
```

There are also attribute forms, one that names a replacement type and one that makes the service fail:

```csharp
[ReplaceService<IEmailSender>(typeof(RecordingEmailSender))]
[FailDependency<IEmailSender>]
```

### 2. Run a substitution in the sample

The sample's application computes every timestamp from `TimeProvider`, so a frozen provider shows its effect in the response. Save this as `FrozenClockJourney.cs` in the sample project. It is your own test, not a committed class. The sample's committed clock coverage lives in `ClockJourney`.

<AnnotatedCode
  filename="FrozenClockJourney.cs"
  code={`[Application(NorthstarTargets.Api)]
[NorthstarMember]
public sealed class FrozenClockJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task TheApplicationUsesTheSubstitutedProvider()
    {
        var frozen = new DateTimeOffset(2030, 1, 15, 12, 0, 0, TimeSpan.Zero);
        Proto.Context.Override<TimeProvider>(() => new FrozenTimeProvider(frozen));

        var name = $"frozen-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(name))
            .PostAsync("/api/v1/projects");

        var project = created
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .ReadRequired<ProjectResponse>();
        Assert.That(
            project.CreatedAtUtc,
            Is.EqualTo(frozen),
            "the application stamped the project from the substituted provider");
    }

    private sealed class FrozenTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}`}
  callouts={[
    {line: 10, title: 'Apply it before the first request', note: 'The override builds a dedicated server for this test.'},
    {line: 13, title: 'The request did not change', note: 'Only the application\'s own provider changed.'},
    {line: 21, title: 'The assertion proves it', note: 'The stamp equals the frozen value only if the replacement reached the application.'},
  ]}
  foot={<>Run it with <code>dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~FrozenClockJourney"</code>.</>}
/>

The test passes. If the substitution had not reached the application, `CreatedAtUtc` would be the real time.

### 3. Read the substitution in the trace

From a run of the sample with the test above:

| Record | Reading |
| --- | --- |
| `service.substitute · TimeProvider`, 53.3 ms | one operation per replacement |
| Server change `substituted`, `aspnetcore.server.substitutions: System.TimeProvider` | the dedicated server records what it carries |
| The response artifact: `createdAtUtc: 2030-01-15T12:00:00+00:00` | the application used the frozen provider |
| `Initialize · ASP.NET Core server · Northstar`, `reused: false` | a new server started for this test |

Durations vary with the machine. The names and the states do not.

### 4. Make it refuse

Add the attribute form below and run it against an address that hosts nothing, so the run cannot hold the application in-process:

```csharp
[ProtoTest]
[SignedInAs]
[ReplaceService<TimeProvider>(typeof(FrozenProvider))]
public Task TheSubstitutionIsRefusedOutOfProcess() => Task.CompletedTask;

public sealed class FrozenProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(2030, 1, 15, 12, 0, 0, TimeSpan.Zero);
}
```

```powershell
$env:ProtoTest__TargetUrl = "http://127.0.0.1:5099"
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~TheSubstitutionIsRefusedOutOfProcess"
Remove-Item Env:ProtoTest__TargetUrl
```

The runner reports one skipped test. The reason names `TimeProvider`, the `Default` application, and the `AddAspNetCoreServer` registration it would need. A substitution that cannot be served skips instead of running against the wrong service.

## What happened

An override does not touch the run's shared server. ProtoTest builds a dedicated server for that test with its substitutions, and releases it when the test ends. One test's replacement cannot leak into the next, and parallel tests that substitute differently each get their own server. The cost is a second server start.

When the application does not run inside the test process, the attribute form skips and the body form, `Override`, throws with the configuration key or registration it needs.

## Check yourself

<Checkpoint
  question="Two tests run in parallel. One overrides TimeProvider with a frozen clock. Which time does the other test's application use, and why?"
  verify={<>Compare the <code>reused: false</code> server under the substitution with the shared server in the run's setup.</>}>

The real time. The override runs on a dedicated server built for that one test. The shared server, which the other test uses, never sees the replacement.

</Checkpoint>

## Remember

- A substitution replaces a service for one test. The shared server never sees it, and the next test starts clean.
- The test runs against a dedicated server, so it pays a second server start.
- It needs the in-process server. Attributes skip when they cannot be served, and `Override` throws.

Next: [run the evidence loop with an agent](/learn/extend/evidence-loop-with-an-agent).

## Go deeper

- [Substituting services per test](/docs/integrations/aspnetcore#substituting-services-per-test): the full contract, named servers (`Server = "Api"`), failed dependencies and every limit.
