# Northstar.ProtoTest

The Learning demo suite for [Northstar](../ProtoTest.SampleApp/README.md). It is the teaching fixture
for the Learn track: every journey here ends in a trace, a report or a failure a reader can read.

## Run it

```bash
dotnet test samples/Northstar.ProtoTest
```

An ordinary run is green and fast. The application is hosted in-process, so the tests control the
clock; the store is a SQLite file under `TestResults/Northstar.ProtoTest/` that each run recreates.
The broker journey skips with a named reason because no broker is configured, and the browser journey
skips when Playwright's Chromium is not installed.

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

## Container mode

The suite owns the containers when asked, so the same tests run against PostgreSQL and RabbitMQ:

```powershell
$env:ProtoTest__Database = "postgres"
$env:ProtoTest__Messaging__Broker = "container"
dotnet test samples/Northstar.ProtoTest
```

Set `ProtoTest__Sample__Drills=true` to let the failure drills fail, then read their traces. Set
`ProtoTest__TargetUrl` to point the suite at a deployed application instead of hosting one.

## Where the application-specific code lives

- `NorthstarAttributes.cs` and `NorthstarMember.cs` group the tenant and the signed-in identity.
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
