---
sidebar_position: 1
title: ProtoTrace
description: "One portable .prototrace file records every hook, client, request, check and state change of a run, ready for the browser viewer."
---

import TraceAnatomy from '@site/src/components/TraceAnatomy';
import TraceDiff from '@site/src/components/TraceDiff';

# ProtoTrace

ProtoTrace is ProtoTest's execution trace. ProtoTest owns the lifecycle and understands its integrations, so it records what happened in every test without a logging line in your tests: hooks, attributes, clients, state, requests, browser actions, assertions, attachments and cleanup. At the end of the run everything is written to one portable `.prototrace` file.

When a test fails in CI, download that file and open it in the [ProtoTrace viewer](https://trace.prototest.dev). You see the failing assertion in context: which user was set up, what the request looked like, what came back, what the browser showed.

## What it is

Tracing is on by default:

```csharp
builder.ConfigureTracing(trace =>
{
    trace.Enabled = true;                                       // default
    trace.OutputPath = "TestResults/billing.prototrace";        // default: TestResults/prototest-{runId}.prototrace
    trace.EmbedArtifacts = true;                                // false declares attachments without their bytes
});
```

With `Enabled = false`, no trace file is written, application spans from the configured sources are not captured, and the in-memory recorder drops operations and records (observations, attachments, findings and tracked values). The run snapshot still lists each test with its outcome, and the reports keep working: run gates read report items, not the operation tree.

The trace is not the same thing as [observations](./coverage.md). The trace is automatic and answers "what did ProtoTest do?". Observations are intentional and answer "what did the test learn?". They share correlation, but observations feed the reports and the trace feeds the viewer.

## How to read it

### In the viewer

The [ProtoTrace viewer](https://trace.prototest.dev) is a static web app. Trace files are read **entirely in your browser** and never uploaded. To look around before you have a trace of your own, [open the sample trace](https://trace.prototest.dev/?demo=1): a run of the demo suite, with a failing test and two partial ones.

- **The run** opens with its verdict, what needs attention (failing and partial tests with the check that decided them, findings, gates), and what the run could see: where the application ran, which capabilities were composed, and which sources of values were present.
- **A failing test** leads with its failure: the check that failed, expected against actual for every property, and the call it judged.
- **Story** tells the test phase by phase: each call carries its checks, and the framework's own steps fold away until you open them.
- **State** shows every tracked item with its lifeline and changes. Select a change to jump to the operation that made it.
- **Spans** is the complete, searchable tree.
- **The inspector** shows everything one operation recorded: where in your code it started, request and response, JSON as a collapsible tree, the shape a check validated, what it changed, and every item's change trail. The address holds the selection, so a link opens the same place.

### Correlating a trace with the run that produced it

Every trace carries the run's own id, its timing and the environment it executed on. When the run happens somewhere you do not control (CI, a shared environment), name the facts that identify it, so a result can be attributed to the build that produced it:

```csharp
builder.ConfigureTracing(trace =>
{
    trace.RunMetadataEnvironmentVariables.Add("GITHUB_RUN_ID"); // read from the process environment
    trace.RunMetadataEnvironmentVariables.Add("GITHUB_SHA");
    trace.RunMetadata["pipeline"] = "nightly";                 // or set a value in code
});
```

Each entry is written as `environment.{key}` on the run's resource group in `spans.json`, next to the built-in `environment.runtime` and `environment.os`, and every [report sink](./reporting.md) receives it as a run-metadata item. A downloaded `.prototrace`, and the report it carries, names the build and run it came from.

The named variables are read once, when the host is built. A variable that is unset or empty, as on a local run, contributes nothing, and neither the trace nor the report changes. An explicit `RunMetadata` value wins over a lifted variable of the same name. ProtoTest does not detect a CI provider by itself: only the variables you name are read, and a name that would hide one of the built-in facts (`runtime`, `os`, `processArchitecture`, `osArchitecture`) fails the build. Values are recorded exactly as given, so list only variables that are safe to carry in a trace.

The viewer's run model reads every `environment.*` entry, but its run header still shows only the runtime and the operating system. Read the metadata from `spans.json` or from the JSON or HTML report's Run metadata section.

### What a trace contains

A run contains tests, and a trace records two things about each of them:

- **What ran**: a tree of **operations** (spans). Each has a duration and an outcome: a request, a flow, a hook. Operations nest, so a `web.flow` contains its clicks and a test's execution contains everything the body did. Moments inside an operation (a server starting, a subscription message) are **events** on it, and so are the observations, attachments and findings it produced.
- **What existed and changed**: the **state**. Every client, context, resource and tracked value, with its state at the end and a trail of changes. Each change names the operation that caused it and where the value came from: the test itself, a response it observed, or the application's own instrumentation.

Tracked values are items with kind `value` and an id of the form `{type}:{identity}`. Test-side provisioning writes the result type in snake_case as the type segment (`InvoiceLine` becomes `invoice_line`, so the item reads `invoice_line:42`), and `{identity}` is what the provisioner returned. An application's own instrumentation writes the prefix of its identity-shaped attribute instead (`invoice.id = 42` contributes `invoice:42`). The two are the same item only when the attribute prefix matches the type segment and the values match, so name a type's identity attribute after the type (`invoice_line.number`) to correlate them.

Every entry belongs to a **phase**:

| Phase | |
| --- | --- |
| `Setup` | hooks and attributes before the test body |
| `Execution` | the test body |
| `Teardown` | hooks, attributes and disposal afterwards |
| `Rollback` | teardown after a *failed* setup |
| `Run` | run-level work |

Every entry also ends with an **outcome**: `Succeeded`, `Failed`, `Partial`, `Cancelled`, `Skipped` or `Unknown`.

A test can pass while something inside it failed (a diagnostic step that is not allowed to fail the run, a best-effort capture). That test is recorded as **Partial** rather than green, so it does not hide in a sea of passing tests.

A few of the entry kinds recorded automatically:

| Kind | From |
| --- | --- |
| `test.setup`, `test.execution`, `test.teardown`, `test.rollback` | the lifecycle |
| `client.initialize`, `client.resolve` | clients: initialization, and a lookup that failed |
| `context.resolve` | typed state: a failed lookup. `SetContext` is a state change on the context entity, not an entry |
| `attachment.publish`, and the `observation` / `attachment` / `finding` records on an operation | attachments, observations and findings |
| `auth.outcome` (`applied` / `skipped`) | HTTP authentication, recorded on the request operation itself |
| `auth.handler.apply` | each handler of a composite authenticator |
| `assert.json.shape` | shape assertions in REST, GraphQL, gRPC and messaging: expected, actual and matched properties |
| `assert.http.status`, `assert.grpc.status` | status assertions |
| `grpc.call`, `grpc.client.resolve`, `grpc.attachment.failed` | the [gRPC client](../integrations/grpc/index.md): calls, fallback resolution and capture failures |
| `messaging.publish`, `messaging.await`, `messaging.attachment.failed` | [publishing and awaiting messages](../integrations/messaging/index.md) |
| `web.navigate`, `web.click`, `web.flow`, `web.login`, `assert.web`, ... | the [browser](../integrations/web/diagnostics.md#what-the-trace-records-for-every-operation) |
| `web.page.visited`, `web.page.verified`, `web.page.available` | [page coverage](../integrations/web/index.md#page-coverage): observations, not operations, for the pages a journey reached, checked and could reach |
| `data.build`, `data.build_many`, `data.create`, `data.create_many`, `data.explain` | [building test data](../integrations/data/index.md) |
| `data.provision`, `data.cleanup`, `data.value.resolve` | [provisioning and cleanup](../integrations/data/provisioners.md) |
| `assert.sheets` | [sheet, range and table assertions](../integrations/sheets/index.md): expected and actual values |
| `sql.connection.open`, `sql.transaction.begin`, `sql.transaction.rollback`, `sql.enlist` | the [SQL connection lifecycle](../integrations/sql/index.md#in-the-trace-and-coverage) |
| `aspnetcore.server.initialize` | the in-process server, carrying `aspnetcore.application.type`, `aspnetcore.server.lifetime`, `aspnetcore.server.reused`, `aspnetcore.web_host.customized` and `aspnetcore.client.customized` |

The in-process server is also a state entity with id `server:{type}` (`{type}` is the entry point's full name, as in `server:Northstar.Api.Program`), and those `aspnetcore.*` attributes are its state.

Sensitive values stay out: form fills are recorded by length, headers and JSON properties are redacted using the [same rules as attachments](../integrations/rest/attachments.md#redaction), and sensitive query parameter values are redacted in HTTP request URLs and web navigation addresses.

### A walk through one test

The sample's `TheTestClockClosesTheDueWindow` was recorded with one of each layer in it. The walk below reads that trace layer by layer, including the parts the same file cannot show.

<TraceAnatomy />

### The same journey, two ways

The Learning demo runs four deliberate failures next to the tests that do the same journey the right way. Pick a pair to compare what the trace recorded in each run.

<TraceDiff />

### Where in the code

Every operation your suite starts (a request, a check, a browser step, a gRPC call) records where in your code it started: the file, the line and the method, as the OpenTelemetry attributes `code.file.path`, `code.line.number` and `code.function.name`. The inspector shows that line with the code around it, so a failed check points at the line that made it.

- The location comes from the stack and your test project's symbols, which the .NET SDK writes by default. ProtoTest's own lifecycle (setup, teardown, the test's execution) records none.
- Inside a git repository the path is relative to its root (`tests/Orders.Tests/OrderTests.cs`), so it reads the same on every machine and does not carry your local directory layout.
- The trace embeds each source file a location points at, so the viewer can show the code without access to the repository.

```csharp
builder.ConfigureTracing(trace =>
{
    trace.CaptureSourceLocations = false; // no locations, and so no embedded code
    trace.EmbedSources = false;           // locations only, no code in the file
});
```

Turn `EmbedSources` off when a trace goes to people who should not read the suite's code.

### Reading a trace in code

The host exposes a live snapshot, which is how ProtoTest's own tests assert on tracing:

```csharp
var run = host.Trace.Snapshot();
var click = run.Tests.Single().Entries.Single(entry => entry.Kind == "web.click");
Assert.That(click.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
```

To add your own entries, see [Extending ProtoTest](../advanced/extending.md#adding-to-the-trace). To forward operations to an observability backend, see [OpenTelemetry](./opentelemetry.md).

`ProtoTraceDiscovery.Discover(folder)` lists the readable runs a folder holds, newest first, and names the archives it had to skip. It is the scan the `prototest` CLI and the MCP server share.

Without a browser, `ProtoTest.Traces` reads the archive and the `prototest` CLI prints the failure digest: `prototest summary TestResults/Shop.prototrace` lists the run, the outcome counts, and every test that did not fully succeed with its error, source location and failing operation. It is the same compact story a CI log or an agent can use.

`prototest index TestResults` writes a static `index.html` over the folder of runs: each run's outcome counts, the tests that did not pass, links to its trace and its `<trace>.digest.json` digest, plus any archive that could not be read and the reason. The page is one file beside the traces, so a folder of evidence can be shared without a server.

A coding agent reads the same story through the MCP server. See [Agent workflows](../agent-workflows/coding-agents.md).

## The artifact

### The file format

A `.prototrace` file is a ZIP archive:

```
run.prototrace
├── manifest.json            { "formatVersion": "2.0", "spansEntry": "spans.json", "stateEntry": "state.json" }
├── spans.json               what ran: one resource group per test and one for the run
├── state.json               what existed and changed: tracked items with their changes
├── sources/1/OrderTests.cs   the code an operation's location points at, when embedded
└── resources/
    ├── {testId}/artifact-1/rest-01-response.json
    ├── {testId}/artifact-2/playwright-default-trace.zip
    └── run/HtmlReportSink/run-artifact-1/report.html
```

- **`spans.json`** holds a resource group per test (its id, name, class, method, outcome and duration) with its operations, their events, and the artifacts the test declared. The run's own group carries its id, start and end, and the environment it ran in (`environment.runtime`, `environment.os`, and any [run metadata you configured](#correlating-a-trace-with-the-run-that-produced-it)); run-level events such as gate verdicts sit on it too.
- **`state.json`** holds the run's tracked items and each test's: kind, id, name, scope, first and last seen, the state at the end and every change, with the operation that caused it.
- **`sources`** in the manifest maps each recorded `code.file.path` to its embedded copy.
- Each artifact is declared once, with its media type, size and path in the archive. An attachment event refers to it by id.

Entries are stored **uncompressed**, so a browser can read the archive without a decompression library. Property names are camelCase. The archive manifest is format **2.0**, its spans document is 2.0, and its state document is **1.1**: tracked values are generic `value` items with the domain type in the id. The live snapshot exposed to code (`host.Trace.Snapshot()`) reports the same span format version.

Test artifacts, every [attachment](../foundation/attachments.md), live under the test's id. Run-level artifacts, such as the reports written by [sinks](./reporting.md), live under `resources/run/`.

### Format compatibility

The format is versioned and tested against `design/prototrace-wire.contract.json`. The reader that ships with the tooling supports the current major and the one before it, and the viewer keeps opening archives it has always opened. A breaking format change bumps the major and arrives with a migration note in the changelog. A reader that meets a format it does not support fails with a message naming the version rather than guessing. `ProtoTest.Traces` follows the same rule: it reads 2.x and rejects anything else explicitly.

### If the process dies

The archive is written once, at the end of the run, after the gates and the reports. A process killed mid-run writes no `.prototrace`. What survives instead is the runner's own output and anything the run already released. An incremental flush (spans per completed test) is not implemented, so if you need evidence from a process that dies, keep the runner's console output and any artifacts the run had already published.

## Limits

- Evidence is written once, at the end. A run that is killed mid-process leaves no archive.
- Redaction covers the framework's own capture (form fills by length, sensitive headers, JSON properties and query parameters). Your own attributes, attachments and [run metadata](#correlating-a-trace-with-the-run-that-produced-it) can still carry application data, so treat a trace like test output.
- Run metadata values are recorded exactly as given. List only variables that are safe to travel in a trace.
- The viewer's run header shows only the runtime and the operating system. Read the other `environment.*` entries from `spans.json` or the report.
- `CaptureSourceLocations` is the largest tracing cost; `EmbedSources` and `EmbedArtifacts` control what the archive carries. The [benchmarks page](../project/benchmarks.md) records what a trace costs at 100 and 1,000 tests and the levers that change it (`EmbedSources`, `EmbedArtifacts`, `MaxArtifactBytes`, capture options).
- A format reader supports the current major and the one before it. An older archive needs a reader from its own era.

## Learn more

- [Coverage and observations](./coverage.md): the intentional facts the reports are built from.
- [Reporting](./reporting.md): the JSON and HTML sinks whose files travel inside the archive.
- [OpenTelemetry](./opentelemetry.md): the same operations, exported to a tracing backend.
- [Extending ProtoTest](../advanced/extending.md#adding-to-the-trace): writing your own entries.
