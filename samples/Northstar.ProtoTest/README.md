# Northstar.ProtoTest

The Learning demo suite for [Northstar](../ProtoTest.SampleApp/README.md). It is the teaching fixture
for the Learn track: every journey here ends in a trace, a report or a failure a reader can read.

## Run it

```bash
dotnet test samples/Northstar.ProtoTest
```

An ordinary run is green and fast, and it ends `Failed: 0, Passed: 14, Skipped: 5, Total: 19` on a
machine with Playwright's Chromium. The application is hosted in-process, so the tests control the
clock; the store is a SQLite file under `TestResults/Northstar.ProtoTest/` that each run recreates.
The broker journey skips with a named reason because no broker is configured, the four drills skip
without their opt-in, and the browser journey skips when Playwright's Chromium is not installed
(`Passed: 13, Skipped: 6` in that case).

If the run reports `Total: 0`, the clone landed on a checkout older than 1.1. The lessons are written
against the 1.1 sample, so fetch that branch before running:

```bash
git fetch origin version/1.1
git checkout version/1.1
```

## The journeys

- `ProjectsJourney` creates, validates and reads projects over REST.
- `ClockJourney` moves the test clock: the application computes every stamp and billing period from
  `TimeProvider`, so nothing waits for real time.
- `BrokerJourney` pays an invoice and awaits the application's `invoice.paid` event.
- `WebJourney` creates a project through the API, signs in on the server-rendered page in a browser
  and reads the project back.
- `PlatformJourney`, `DomainAccessJourney` and `SheetsJourney` cross protocols: REST to GraphQL, REST
  to the database, and a downloaded workbook.
- `FailureDrills` holds four deliberate failures, one per failure mode (time, state, environment,
  visibility), each paired with the green test that does the journey the right way.

## What the lessons embed

`eng/generate-lesson-traces.ps1` runs each test below and writes the trace to `docs/static/lessons/`.
A lesson embeds the file it names, so the evidence is the run's own and a lesson never invents a
failure.

| Trace | Test | What the run proves | Used by |
| --- | --- | --- | --- |
| `l0-time-drill.prototrace` | `FailureDrills.ARealWaitDoesNotCloseTheDueWindow` | The drill fails: a real second does not close the test clock's due window. | L0 `the-four-questions`, L0 `a-failure-tour` (`FailureGallery`), L4 `read-a-failing-trace` (`TraceDiff`), L6 `evidence-loop-with-an-agent` |
| `l0-time-fix.prototrace` | `FailureDrills.TheTestClockClosesTheDueWindow` | The fix passes: `Clock.Advance` closes the window and paying the invoice succeeds. | L0 `a-failure-tour` (`FailureGallery`), L1 `read-the-trace` (`TraceAnatomy`), L4 `read-a-failing-trace` (`TraceDiff`) |
| `l0-state-drill.prototrace` | `FailureDrills.AnUnknownProjectIdIsTreatedAsMine` | The drill fails: the fixed id `prj_1` belongs to no test in the run. | L0 `a-failure-tour` (`FailureGallery`), L2 `when-not-to-compose`, L4 `read-a-failing-trace` (`TraceDiff`) |
| `l0-state-fix.prototrace` | `FailureDrills.EachTenantSeesOnlyItsOwnProjects` | The fix passes: the test lists only the project it created in its own tenant. | L0 `what-a-test-leaves-behind`, L0 `a-failure-tour` (`FailureGallery`), L2 `when-not-to-compose`, L3 `per-test-state-and-cleanup`, L3 `parallel-safety`, L4 `read-a-failing-trace` (`TraceDiff`) |
| `l0-environment-drill.prototrace` | `FailureDrills.TheAddressWasHardcodedForOneMachine` | The drill fails outside the composition; the trace records no operation for the raw client. | L0 `a-failure-tour` (`FailureGallery`), L0 `the-trace-as-the-feedback-loop`, L4 `read-a-failing-trace` (`TraceDiff`) |
| `l0-environment-fix.prototrace` | `FailureDrills.TheAddressComesFromTheComposition` | The fix passes: the same call through the composed client, address and all. | L0 `a-failure-tour` (`FailureGallery`), L4 `read-a-failing-trace` (`TraceDiff`) |
| `l0-visibility-drill.prototrace` | `FailureDrills.ABareStatusHidesWhatTheApplicationSaid` | The drill fails on the status alone; the body that named the problem goes unread. | L0 `a-failure-tour` (`FailureGallery`), L0 `the-trace-as-the-feedback-loop`, L4 `read-a-failing-trace` (`TraceDiff`) |
| `l0-visibility-fix.prototrace` | `FailureDrills.TheProblemBodyNamesTheCodeAndDetail` | The fix passes: the problem body is asserted, so the failure message names the code. | L0 `a-failure-tour` (`FailureGallery`), L0 `the-trace-as-the-feedback-loop`, L4 `read-a-failing-trace` (`TraceDiff`) |
| `l1-first-journey.prototrace` | `ProjectsJourney.CreatingAProjectReturnsIt` | One REST call with its request and response artifacts and the shape check. | L1 `write-your-first-test`, L1 `read-the-trace`, L2 `capabilities-and-the-host`, L6 `attributes`, L6 `provisioners-and-page-objects`, L6 `write-an-integration` |
| `l2-broker-skip.prototrace` | `BrokerJourney.PayingAnInvoicePublishesAnInvoicePaidEvent` | The `Broker` capability is absent without a broker; the gated journey never starts. | L2 `add-and-remove-an-integration` |
| `l3-clock-window.prototrace` | `ClockJourney.ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock` | The test clock closes the billing period; the run also carries the `/health` readiness entity. | L3 (`the-test-clock`, `readiness-instead-of-sleeps`, `parallel-safety`) |
| `l4-coverage.prototrace` | `PlatformJourney.RestWritesAreVisibleThroughGraphQL` | One REST write and one GraphQL read; the embedded report carries the contract coverage row the run recorded. | L4 `contract-coverage` |
| `l4-artifacts.prototrace` | `SheetsJourney.TheMonthlyReport_ShouldMatchItsModel` | A downloaded workbook with its response artifact and the model assertions. | L4 `artifacts-and-reports` |

## Start from a clean file

The first lesson writes its own test, runs it and reads the trace. The path is:

1. Add `samples/Northstar.ProtoTest/MyFirstJourney.cs`:

```csharp
namespace Northstar.ProtoTest;

using System.Net;
using global::ProtoTest.Core;
using global::ProtoTest.Http;
using global::ProtoTest.NUnit;
using global::ProtoTest.Rest;
using global::ProtoTest.SampleApp.Contracts;

[Application(NorthstarTargets.Api)]
[NorthstarMember]
public sealed class MyFirstJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task CreatingAProjectReturnsIt()
    {
        var name = $"first-{Proto.Context.TestId}";
        using var created = await Proto.Context.Rest()
            .Body(new CreateProjectRequest(name))
            .PostAsync("/api/v1/projects");

        created
            .Should.HaveHttpStatus(HttpStatusCode.Created)
            .Should.MatchShape(new { name, status = ProjectStatuses.Active });
    }
}
```

2. Run it from the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
```

3. Open `samples/Northstar.ProtoTest/bin/Debug/net8.0/TestResults/Northstar.ProtoTest/northstar.prototrace`
   in the [trace viewer](https://trace.prototest.dev). The committed archive
   `l1-first-journey.prototrace` is the sample's own version of this journey: the same layers, one
   REST create and a shape check, with `atlas-...` names and more asserted fields. Compare the shape,
   not the values.

4. Keep the file for the Level 6 lessons, which apply an attribute and a milestone to it, or delete
   it when you are done: `rm MyFirstJourney.cs`. The suite is unchanged and `git status` is clean.

## Determinism in this suite

The determinism lessons read these from the suite itself:

- Readiness: `Setup.cs` registers `AddHttpReadiness` on `/health` for the loopback application, after
  the application it probes. No test sleeps waiting for the application to come up.
- The clock: the in-process server hands the application the test's `TimeProvider`, and `Setup.cs`
  removes the domain's own provider so the bridge wins. `ClockJourney` and the time pair in
  `FailureDrills` move that clock instead of waiting.
- Parallel safety: `AssemblyInfo.cs` runs the suite with `ParallelScope.All` and eight workers. Each
  test provisions its own tenant through `context.UniqueName`, so fixed names inside a tenant cannot
  meet another test's, and the records that leave the tenant carry `context.TestId`. The traces show
  the per-test tenant cleanup.

## Container mode

Docker must be running: the suite starts the PostgreSQL and RabbitMQ containers it owns, so the same
tests run against them:

```powershell
$env:ProtoTest__Database = "postgres"
$env:ProtoTest__Messaging__Broker = "container"
dotnet test samples/Northstar.ProtoTest
```

Set `ProtoTest__Sample__Drills=true` to let the failure drills fail, then read their traces. Set
`ProtoTest__TargetUrl` to point the suite at a deployed application instead of hosting one.

## Where the application-specific code lives

- `NorthstarAttributes.cs` holds the tenant and signed-in identity attributes; `NorthstarMember.cs` resolves the signed-in member's token.
- `NorthstarAuthenticator.cs` carries the member's bearer token through REST and GraphQL.
- `NorthstarData.cs` and the `Provisioners/` folder create fixtures through the API or the domain.
- `NorthstarScenario.cs` is a hook that correlates every test and attaches a summary.
- `Setup.cs` composes the run; `NorthstarRun.cs` names the store and broker choices.

## Evidence

Each run writes `northstar.prototrace`, `report.html` and `report.json` under
`TestResults/Northstar.ProtoTest/`. Open the report for route coverage and traffic, and drop the
trace on [trace.prototest.dev](https://trace.prototest.dev) to walk the journeys.

## Learn more

- [Documentation](https://prototest.dev/)
- [Learn](https://prototest.dev/learn)
