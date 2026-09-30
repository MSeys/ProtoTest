---
id: swap-a-dependency-for-one-test
title: Swap a dependency for one test
sidebar_label: Swap a dependency
sidebar_position: 4
description: "Replace a service in the application under test for one test, read the dedicated server in the trace, and name the cases where the framework refuses."
---

import LearnShell from '@site/src/components/LearnShell';
import AnnotatedCode from '@site/src/components/AnnotatedCode';
import Link from '@docusaurus/Link';

# Swap a dependency for one test

In Level 5 one outbox test replaced the API's event publisher with a failing one, and the rest of the path stayed real. This lesson teaches the seam behind it: what a substitution changes, what it costs, and the cases where the framework refuses.

<LearnShell
  level="Level 6, lesson 4"
  minutes="About 10 minutes"
  outcome={[
    'Replace a service in the application under test for one test with context.Override.',
    'Read the substitution and the dedicated server it builds in the trace.',
    'Name the cases where a substitution skips or throws, and why.',
  ]}
  before={[
    <>Write an integration (<Link to="/learn/make-it-yours/write-an-integration">lesson 3</Link>).</>,
    'The sample cloned. The substitution run needs nothing else installed.',
  ]}
  situation={
    <>
      <p>The outbox tests of the OpenCSMS product swap <code>IEventPublisher</code> for one test: the replacement fails a bounded number of publishes and delegates every later attempt to the real publisher. Nothing else in that path is faked.</p>
      <p>The seam is <code>context.Override</code>. It needs an application the run hosts in-process, because the replacement is registered in the application's own container. Where there is no container the framework says so instead of pretending: the attribute form skips, the body form throws. This lesson runs a substitution in the sample and reads both outcomes.</p>
    </>
  }
  checkpoint={{
    question:
      'A suite hosts Api in-process and points Web at a published address. A test carries [ReplaceService&lt;T&gt;(...)] with no Server set and selects Web. What does the runner report, and why does the live Api server not change it?',
    verify: (
      <>
        Read <a href="/docs/integrations/aspnetcore#substituting-services-per-test">Substituting services per test</a> on the ASP.NET Core page, then run the attribute form with <code>ProtoTest__TargetUrl=http://127.0.0.1:5099</code>. The runner prints the skip and its reason.
      </>
    ),
    reveal: (
      <>
        It skips with a reason naming Web: the unnamed gate follows the test's selected application, and a configured <code>BaseUrl</code> drops the in-process capability the substitution needs. The live Api server is irrelevant, because the gate resolves the selected application and not any server that happens to be alive. Naming <code>Server = "Api"</code> would run the substitution there; the body form, <code>Override</code>, has no gate and throws instead.
      </>
    ),
  }}
  learned={[
    'A substitution replaces a service for one test; the run\'s shared server never sees it and the next test starts clean.',
    'The test runs against a dedicated server built with the union of its substitutions, so it pays a second server start.',
    'Substitution needs the in-process server: attributes gate and skip, and Override throws naming the address or the missing registration.',
  ]}
  next={[
    {
      label: 'Run the evidence loop with an agent',
      to: '/learn/make-it-yours/evidence-loop-with-an-agent',
      note: 'The trace you just learned to read, read by a coding agent over MCP.',
    },
    {
      label: 'Substituting services per test',
      to: '/docs/integrations/aspnetcore#substituting-services-per-test',
      note: 'The full contract, the attributes and every limit.',
    },
  ]}>

## The seam

The fault injection lesson used it in one line:

```csharp
Proto.Context.Override<IEventPublisher>(publisher);
```

The same seam has an attribute form and a type form:

```csharp
[ReplaceService<IEmailSender>(typeof(RecordingEmailSender))]
[FailDependency<IEmailSender>]
```

A test can declare substitutions on the class, on the method, and in the body. They combine into one set. The later registration wins. The test then runs against a **dedicated server** built with that union before it starts. The run's shared server is never reconfigured, one test's replacement cannot leak into the next, and parallel tests that substitute differently each get their own instance. A failed dependency is the error-path twin: resolving it throws instead of returning a fake.

## Run one in the sample

The sample's application computes every stamp from `TimeProvider`, so a frozen provider is a substitution whose effect is visible in the response. Add this test:

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
    {line: 10, title: 'Apply it before the first request', note: 'The dedicated server is built when the override lands and application services are resolved after it. The factory form builds the instance lazily; the instance and type forms exist too.'},
    {line: 13, title: 'The test did not change', note: 'It still sends one request through the composed client. Only the application\'s own provider changed.'},
    {line: 21, title: 'The assertion proves the app resolved it', note: 'The stamp comes from the application, so it can only equal the frozen value if the replacement reached it.'},
  ]}
  foot={<>Save the file above as <code>FrozenClockJourney.cs</code> in the sample project. It is your own test, not a committed class: the sample's committed clock coverage lives in <code>ClockJourney</code>. Then run it with <code>dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~FrozenClockJourney"</code>.</>}
/>

## What the run records

The substitution is a traced operation, and the dedicated server says what it was built with. From a run of the sample with the test above:

| Record | Reading |
| --- | --- |
| `service.substitute · TimeProvider`, 53.3 ms, with `service.type`, `service.server` and `service.replacement` | one operation per replacement, linked to the server entity |
| server entity change `substituted`: `aspnetcore.server.substituted: true`, `aspnetcore.server.substitutions: System.TimeProvider` | the dedicated instance records what it carries |
| the response artifact: `createdAtUtc: 2030-01-15T12:00:00+00:00` | the application really resolved the frozen provider |
| `Initialize · ASP.NET Core server · Northstar` under the substitution, `reused: false` | the dedicated instance is started for this test, not the shared one |
| the run's shared server initialization in setup: 291.2 ms; the dedicated start inside the substitution: 53.3 ms | the substituting test pays a second start |

Durations vary with the machine; the names and the states do not.

## Where it cannot substitute

The seam is only as wide as the in-process server it threads through. The attribute form gates and skips; the body form throws:

| Case | What happens |
| --- | --- |
| The selected application is hosted in-process | the substitution runs; the test gets a dedicated server |
| `[ReplaceService]` or `[FailDependency]` with `Server = "Api"` | gates on that named in-process server |
| Neither, with a selected application | gates on the selected application, falling back to `Default` |
| The selected application is published, `BaseUrl` set | the attribute skips with a reason naming the application; another live server does not keep the gate open |
| A container or loopback application | no service container to reach; the attribute skips |

| Case | What happens |
| --- | --- |
| Body `Override`, application published | throws: `Application 'Api' runs at '<address>', so its services cannot be substituted`, naming the configured `ProtoTest:Applications:Api:BaseUrl` key |
| Body `Override`, container or loopback application | throws, naming the missing `AddAspNetCoreServer` registration |
| The test resolved application services first | that scope stays on the shared server; apply the override before the first resolution |
| A failed dependency resolved while the server starts | the setup fails with the application's own exception; prefer failing services resolved per request |

The replacement lives as a singleton of the dedicated server and is released with the test.

To see the skip in the sample, add the attribute form and run it against an address that hosts nothing:

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

The runner reports one skipped test with the reason: this test substitutes `TimeProvider` on the `Default` application, which this run does not host in-process, so register it with `AddAspNetCoreServer` or name the in-process server in a mixed run. The probe carries no `[Application]`, so the gate falls back to `Default`. Nothing ran and nothing failed, which is the honest outcome for a substitution that cannot be served.

</LearnShell>
