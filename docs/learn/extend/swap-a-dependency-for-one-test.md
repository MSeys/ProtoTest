---
id: swap-a-dependency-for-one-test
title: Swap a dependency for one test
sidebar_label: Swap a dependency
sidebar_position: 4
description: "Replace one service in the application under test for a single test, read the dedicated server in the trace, and see when the framework refuses."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import AnnotatedCode from '@site/src/components/AnnotatedCode';

# Swap a dependency for one test

<Lesson
  track="Extend ProtoTest"
  step="Lesson 4 of 5"
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

The seam for this is `Override`. It works only when the run hosts the application in-process, because the replacement is registered in the application's own service container. When there is no container the framework says so: the attribute form skips, and the body form throws.

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
    {line: 10, title: 'Apply it before the first request', note: 'The override builds a dedicated server, and application services are resolved after it. This form takes a factory, so the instance is built lazily.'},
    {line: 13, title: 'The request did not change', note: 'The test still sends one request through the composed client. Only the application\'s own provider changed.'},
    {line: 21, title: 'The assertion proves it', note: 'The stamp comes from the application, so it equals the frozen value only if the replacement reached it.'},
  ]}
  foot={<>Run it with <code>dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~FrozenClockJourney"</code>.</>}
/>

The test passes. If the substitution had not reached the application, `CreatedAtUtc` would be the real time.

### 3. Read the substitution in the trace

From a run of the sample with the test above:

| Record | Reading |
| --- | --- |
| `service.substitute · TimeProvider`, 53.3 ms, with `service.type`, `service.server` and `service.replacement` | One operation per replacement, linked to the server entity. |
| Server entity change `substituted`: `aspnetcore.server.substituted: true`, `aspnetcore.server.substitutions: System.TimeProvider` | The dedicated server records what it carries. |
| The response artifact: `createdAtUtc: 2030-01-15T12:00:00+00:00` | The application resolved the frozen provider. |
| `Initialize · ASP.NET Core server · Northstar` under the substitution, `reused: false` | A new server started for this test, not the shared one. |
| Shared server initialization in setup: 291.2 ms. Dedicated start inside the substitution: 53.3 ms | The substituting test pays for a second server start. |

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

The runner reports one skipped test. The reason says this test substitutes `TimeProvider` on the `Default` application, which this run does not host in-process, so register it with `AddAspNetCoreServer` or name the in-process server in a mixed run. The test has no `[Application]`, so the gate falls back to `Default`. Nothing ran and nothing failed, which is the honest outcome for a substitution that cannot be served.

## What happened

An override does not touch the run's shared server. ProtoTest builds a dedicated server for that test with its substitutions, and releases it when the test ends. One test's replacement cannot leak into the next, and parallel tests that substitute differently each get their own server.

That is also the cost: a substituting test pays a second server start.

Substitutions from the class, the method and the body combine into one set, and the later registration wins. When the application is not in-process, the two forms refuse differently:

| Case | What happens |
| --- | --- |
| Attribute, application published (`BaseUrl` set) | The test skips with a reason naming the application. |
| Attribute, container or loopback application | The test skips: there is no service container to reach. |
| Body `Override`, application published | Throws `Application 'Api' runs at '<address>', so its services cannot be substituted`, naming the `ProtoTest:Applications:Api:BaseUrl` key. |
| Body `Override`, container or loopback application | Throws, naming the missing `AddAspNetCoreServer` registration. |

## Check yourself

<Checkpoint
  question="A suite hosts Api in-process and points Web at a published address. A test carries [ReplaceService<T>(...)] with no Server set and selects Web. What does the runner report, and why does the live Api server not change it?"
  verify={<>Run the attribute form with <code>ProtoTest__TargetUrl=http://127.0.0.1:5099</code> as in step 4 and read the skip reason.</>}>

It skips with a reason naming Web. With no <code>Server</code> set, the gate follows the test's selected application, and a configured <code>BaseUrl</code> drops the in-process capability the substitution needs. The live Api server is irrelevant, because the gate resolves the selected application and not any server that happens to be running. Naming <code>Server = "Api"</code> would run the substitution there. The body form, <code>Override</code>, has no gate and throws instead.

</Checkpoint>

## Remember

- A substitution replaces a service for one test. The shared server never sees it, and the next test starts clean.
- The test runs against a dedicated server, so it pays a second server start.
- It needs the in-process server. Attributes skip when they cannot be served, and `Override` throws.

Next: [run the evidence loop with an agent](/learn/extend/evidence-loop-with-an-agent).

## Go deeper

- [Substituting services per test](/docs/integrations/aspnetcore#substituting-services-per-test): the full contract, named servers (`Server = "Api"`), failed dependencies and every limit.
