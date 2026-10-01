# Northstar.ProtoTest

The Learning demo suite for [Northstar](../ProtoTest.SampleApp/README.md). Every journey here ends in a trace, a report or a failure a reader can read. Start with the [Learn track](https://prototest.dev/learn).

## Run it

```bash
dotnet test samples/Northstar.ProtoTest
```

An ordinary run is green. The application is hosted in-process and the store is recreated per run. Environment-dependent journeys skip with a named reason (broker, drills, browser).

The lessons match the checked-out release. If the run reports `Total: 0`, check out the matching release branch first.

## The journeys

- `ProjectsJourney` creates, validates and reads projects over REST.
- `ClockJourney` moves the test clock: the application computes every stamp and billing period from `TimeProvider`, so nothing waits for real time.
- `WebhookJourney` creates a project and polls the delivery list until the background dispatcher reports its webhook delivered. Its drill reads once instead, so it passes or fails by timing; `Northstar__WebhookDispatchInterval` slows the dispatcher to make it fail every time.
- `BrokerJourney` pays an invoice and awaits the application's `invoice.paid` event.
- `WebJourney` creates a project through the API, signs in on the server-rendered page in a browser and reads the project back.
- `PlatformJourney`, `DomainAccessJourney` and `SheetsJourney` cross protocols: REST to GraphQL, REST to the database, and a downloaded workbook.
- `FailureDrills` holds four deliberate failures, one per failure mode (time, state, environment, visibility), each paired with the green test that does the journey the right way, plus one passing journey that carries a warning, so the run records a partial outcome and a finding.

## Write your first journey

Add `samples/Northstar.ProtoTest/MyFirstJourney.cs`:

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

Run it from the repository root:

```bash
dotnet test samples/Northstar.ProtoTest --filter "FullyQualifiedName~MyFirstJourney"
```

Open `samples/Northstar.ProtoTest/bin/Debug/<tfm>/TestResults/prototest-{runId}.prototrace` (each run writes its own file, named after the run id) in the [trace viewer](https://trace.prototest.dev). The committed archive `l1-first-journey.prototrace` is the sample's own version of this journey: the same layers, one REST create and a shape check, with `atlas-...` names and more asserted fields. Compare the shape, not the values.

Keep the file for the Level 6 lessons, which apply an attribute and a milestone to it, or delete it when you are done: `rm MyFirstJourney.cs`. The suite is unchanged and `git status` is clean.

## Read the run

Each run writes its own `prototest-{runId}.prototrace` under `TestResults/`, plus `report.html` and `report.json` under `TestResults/Northstar.ProtoTest/`. Open the report for route coverage and traffic, and drop the trace on [trace.prototest.dev](https://trace.prototest.dev) to walk the journeys.

## Container mode

Docker must be running: the suite starts the PostgreSQL and RabbitMQ containers it owns, so the same tests run against them:

```powershell
$env:ProtoTest__Database = "postgres"
$env:ProtoTest__Messaging__Broker = "container"
dotnet test samples/Northstar.ProtoTest
```

Set `ProtoTest__Sample__Drills=true` to let the failure drills fail, then read their traces. Set `ProtoTest__TargetUrl` to point the suite at a deployed application instead of hosting one.

## Where the application-specific code lives

- `NorthstarAttributes.cs` holds the tenant and signed-in identity attributes; `NorthstarMember.cs` resolves the signed-in member's token.
- `NorthstarAuthenticator.cs` carries the member's bearer token through REST and GraphQL.
- `NorthstarData.cs` and the `Provisioners/` folder create fixtures through the API or the domain.
- `NorthstarScenario.cs` is a hook that correlates every test and attaches a summary.
- `Setup.cs` composes the run; `NorthstarRun.cs` names the store and broker choices.

## Determinism in this suite

The determinism lessons read these from the suite itself:

- Readiness: `Setup.cs` registers `AddHttpReadiness` on `/health` for the loopback application, after the application it probes. No test sleeps waiting for the application to come up.
- The clock: the in-process server hands the application the test's `TimeProvider`, and `Setup.cs` removes the domain's own provider so the bridge wins. `ClockJourney` and the time pair in `FailureDrills` move that clock instead of waiting.
- Parallel safety: `AssemblyInfo.cs` runs the suite with `ParallelScope.All` and eight workers. Each test provisions its own tenant through `context.UniqueName`, so fixed names inside a tenant cannot meet another test's, and the records that leave the tenant carry `context.TestId`. The traces show the per-test tenant cleanup.

## Lesson traces (for lesson authors)

`./proto traces lessons` runs each test below and writes the trace to `docs/static/lessons/`. A lesson embeds the file it names, so the evidence is the run's own and a lesson never invents a failure.

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
| `l1-first-journey.prototrace` | `ProjectsJourney.CreatingAProjectReturnsIt` | One REST call with its request and response artifacts and the shape check. | L1 `write-your-first-test`, L1 `read-the-trace`, L2 `capabilities-and-the-host`, L2 `one-host-one-lifetime`, L2 `sign-in-as-a-test-user`, L6 `attributes`, L6 `provisioners`, L6 `write-an-integration` |
| `l2-broker-skip.prototrace` | `BrokerJourney.PayingAnInvoicePublishesAnInvoicePaidEvent` | The `Broker` capability is absent without a broker; the gated journey never starts. | L2 `add-and-remove-an-integration`, L2 `one-host-one-lifetime` |
| `l3-clock-window.prototrace` | `ClockJourney.ClosingTheBillingPeriodIssuesTheInvoiceOnTheTestClock` | The test clock closes the billing period; the run also carries the `/health` readiness entity. | L3 (`the-test-clock`, `readiness-instead-of-sleeps`, `parallel-safety`) |
| `l3-lagging-read.prototrace` | `WebhookJourney.CreatingAProjectDeliversItsWebhook` | The poll passes: one REST read per probe until the webhook delivery is reported. | L3 `wait-for-a-lagging-read` |
| `l4-flaky-pass.prototrace`, `l4-flaky-fail.prototrace` | `WebhookJourney.OneReadRacesTheDispatcher` (drill) | The same drill passes in one run and fails in another: one read races the webhook dispatcher. The failing run sets `Northstar__WebhookDispatchInterval=00:00:02`, which makes the drill fail every time; the passing run re-runs until it passes. | L4 `diagnose-a-flaky-test` |
| `l4-coverage.prototrace` | `PlatformJourney.RestWritesAreVisibleThroughGraphQL` | One REST write and one GraphQL read; the embedded report carries the contract coverage row the run recorded. | L4 `contract-coverage`, L4 `read-the-findings-and-the-run-gate` |
| `l4-artifacts.prototrace` | `SheetsJourney.TheMonthlyReportMatchesItsModel` | A downloaded workbook with its response artifact and the model assertions. | L4 `artifacts-and-reports` |
| `l4-partial.prototrace` | `FailureDrills.APassingJourneyCanStillCarryAWarning` | The journey passes but names the fields it left unread: the run records a partial outcome and a Warning finding, and the `no error findings` gate still passes. | L4 `read-the-findings-and-the-run-gate` |

## Learn more

- [Documentation](https://prototest.dev/)
- [Learn](https://prototest.dev/learn)
