---
sidebar_position: 1
title: ProtoTrace
description: "One portable .prototrace file records every hook, client, request, check and state change of a run, ready for the browser viewer."
---

import TraceAnatomy from '@site/src/components/TraceAnatomy';
import TraceDiff from '@site/src/components/TraceDiff';
import ViewerWalkthrough from '@site/src/components/ViewerWalkthrough';

# ProtoTrace

ProtoTest records each test on its own. No logging calls are needed. The trace holds hooks, requests, checks, state changes, attachments and cleanup, and the run writes it to one portable `.prototrace` file. Portable has a limit: a reader only opens an archive from its own era, so check [format compatibility](./prototrace-archive.md#format-compatibility) before you archive traces long term.

When a test fails in CI, download that file and open it in the [ProtoTrace viewer](https://trace.prototest.dev). You see the failing check with the request, the response and the setup around it. Without a browser, read the same story from a terminal:

```bash
dotnet tool install --global ProtoTest.Cli
prototest summary TestResults/prototest-{runId}.prototrace
```

```text
ProtoTest trace 2.0 · run bb2dd8b330924038894c161efda1e5f4 · 2026-09-29 18:36:55Z - 2026-09-29 18:36:58Z
1 tests · 1 failed

FAILED Northstar.ProtoTest.FailureDrills.TheAddressWasHardcodedForOneMachine (2.60 s)
  ConnectionError reaching http://127.0.0.1:5099: connection refused.
  test.execution Test execution · failed
  cause: runner-reported failure
```

That is a committed failing run of the Learning track. Run ids and timestamps are new on every run; compare the shape, not the values. [Open the sample trace](https://trace.prototest.dev/?demo=1) to look around before you have one of your own, and read [the archive format](./prototrace-archive.md#the-file-format) for what is inside.

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

The trace is not the same thing as [observations](./coverage.md). The trace is automatic and answers "what did ProtoTest do?". Observations are facts a test chose to record. They share correlation, but observations feed the reports and the trace feeds the viewer.

## How to read it

### In the viewer

The [ProtoTrace viewer](https://trace.prototest.dev) is a static web app. Trace files are read **entirely in your browser** and never uploaded. To look around before you have a trace of your own, [open the sample trace](https://trace.prototest.dev/?demo=1): a run of the sample suite, with four failing tests and one partial one.

- **The run** leads one test list with shared search and outcome filters. Needs attention names the diagnosis rule: Assertion, Operation error, Runner failure or Finding, using the same precedence as `prototest summary`. The run timeline places tests and their gaps on the run clock. Run operations and tracked items open in the inspector; Run details lists the run id and every `environment.*` value.
- **A failing test** starts with a verdict bar: the diagnosis rule, the check or operation that decided it, the call it judged, and the first difference. The inspector holds the full comparison.
- **Steps** opens on the test body. Setup and teardown fold into summary rows and open along a failure. Calls carry their checks; time with no recorded operation gets its own row.
- **Timeline** is every operation on the test clock. Zoom to one phase, search by name, kind or attribute, and filter for needs attention. Matching operations keep their ancestors for context.
- **State** shows tracked items with lifelines and changes on that same clock. Select a change to open its operation, shade its time and highlight the items it touched. Select an item to see its trail; its operations are highlighted in Timeline.
- **Evidence** brings observations, files, findings and moments into time order. Each names the operation that recorded it, or states that none was above it.
- **The inspector** shows the source, request and response, comparisons, state changes and evidence. Moments include their attributes and sections, observations their metadata, and findings their category, tags, target and metadata. A section index jumps to each part. Binary bodies recorded as text are marked instead of shown as broken glyphs.

The **Framework** switch beside the view tabs shows, dims or hides the framework's own operations (hooks, extensions, clients, resources) in Steps and Timeline. Dim is the default, the choice is remembered, and a framework operation that failed always stays.

The header follows the path Run > Test > Operation. Test views use `#/test/<id>/steps|timeline|state|evidence`; old `story`, `spans` and `files` links still open the corresponding view. The URL holds the selected operation or item, including selections on the run, so a shared link keeps that place.

Hatched time means **no operation was recorded**, not that nothing happened. The viewer derives gaps inside a phase from 15% of its duration, at least 20 ms, and always from 250 ms. Test 12 in the sample has a one-second real wait; test 15 waits for an address that cannot be reached. Both become visible gaps without changing the trace format.

These excerpts follow test 12 from the run through its four views and into the failing check:

<ViewerWalkthrough />

### Open your own archive

A run leaves its archive at `TestResults/prototest-{runId}.prototrace` under the test project's output folder, or at the path you set with `trace.OutputPath`. From there:

1. Run the suite once so the file exists.
2. Open it in the [viewer](https://trace.prototest.dev): drop the file on the page, or press **Open trace** and choose it. The file is read in your browser and never uploaded.
3. Without a browser, read the same story from the terminal:

```bash
prototest summary TestResults/prototest-{runId}.prototrace
prototest index TestResults
```

`prototest summary` lists the run, the outcome counts and every test that did not fully pass, with its error, source location and the failing operation. `prototest index` writes one static `index.html` beside the runs: each run's outcome counts, its failing tests, links to its trace and digest, and every archive that could not be read. The page is one file beside the traces, so a folder of evidence can be shared without a server. Both verbs are the [ProtoTest.Cli](../agent-workflows/cli.md) tool; install it with `dotnet tool install --global ProtoTest.Cli`.

### Run the viewer locally

The hosted viewer is the same static app as the repository's `viewer/` folder, so an offline or air-gapped setup runs it without the trace ever leaving the machine:

```bash
cd viewer
npm ci              # once, where the registry is reachable
npm run build       # writes the static dist/ folder
npm run preview     # serves dist/ locally
```

Then open the served page and drop the `.prototrace` archive on it, or press **Open trace** and choose it. The file is read in browser memory and never uploaded, exactly like the hosted viewer. For a closed network, build once where `npm ci` reaches the registry and carry the `dist/` folder over: it needs no server runtime, so any static file server in front of it works. The archive to open is the run's file at `TestResults/prototest-{runId}.prototrace`, or the path `trace.OutputPath` set. `npm run dev` instead starts the development server when you work on the viewer itself.

### What a trace contains

A run contains tests, and a trace records two things about each of them:

- **What ran**: a tree of **operations** (spans). Each has a duration and an outcome: a request, a flow, a hook. Operations nest, so a `web.flow` contains its clicks and a test's execution contains everything the body did. Moments inside an operation (a server starting, a subscription message) are **events** on it, and so are the observations, attachments and findings it produced.
- **What existed and changed**: the **state**. Every client, context, resource and tracked value, with its state at the end and a trail of changes. Each change names the operation that caused it and where the value came from: the test itself, a response it observed, or the application's own instrumentation.

One test, one operation and its records, the shape every section of a trace repeats:

```text
test.execution  Execution · Succeeded · 314 ms
├── data.provision                       Provision invoice · Succeeded
│   └── state change: invoice:42         created by the test
├── http.request  GET /api/orders/42      Succeeded
│   ├── assert.http.status  200           Succeeded
│   └── assert.json.shape   3 properties  Succeeded
└── events on test.execution
    ├── observation  invoice.state = paid
    └── attachment   00001-rest-01-response.json
```

Every entry belongs to a **phase**:

| Phase | |
| --- | --- |
| `Setup` | hooks and attributes before the test body |
| `Execution` | the test body |
| `Teardown` | hooks, attributes and disposal afterwards |
| `Rollback` | teardown after a *failed* setup |
| `Run` | run-level work |

Every entry also ends with an **outcome**: `Succeeded`, `Failed`, `Partial`, `Cancelled`, `Skipped` or `Unknown`.

A test can pass while something inside it failed (a diagnostic step that is not allowed to fail the run, a best-effort capture). That test is recorded as **Partial**, not Passed.

Tracked values are items with kind `value` and an id of the form `{type}:{identity}`. Test-side provisioning uses the result type in snake_case as the type segment. `InvoiceLine` becomes `invoice_line`, and the item reads `invoice_line:42`. The identity is what the provisioner returned. An application's own instrumentation writes the prefix of its identity-shaped attribute instead (`invoice.id = 42` contributes `invoice:42`). The two are the same item only when the attribute prefix matches the type segment and the values match, so name a type's identity attribute after the type (`invoice_line.number`) to correlate them.

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

The in-process server is also a state entity with id `server:{type}:{application}` (`{type}` is the entry point's full name, as in `server:ProtoTest.SampleApp.Program:Northstar`), and those `aspnetcore.*` attributes are its state.

ProtoTest's own capture redacts by name: form fills are recorded by length, headers and JSON properties are redacted using the [same rules as attachments](../integrations/rest/attachments.md#redaction), and sensitive query parameter values are redacted in HTTP request URLs and web navigation addresses. Findings, observations and attachments you record yourself are redacted only where you mark them sensitive, so treat a trace like test output.

A suite can name more values sensitive. `ConfigureRedaction` adds names to the defaults, and state values and finding metadata redact them:

```csharp
builder.ConfigureRedaction(redaction => redaction.AddSensitiveName("OwnerToken"));
```

The names travel with the host: a second host in the same process keeps the defaults only. `ProtoTest:Redaction` binds the same names from configuration. See [Configuration](../getting-started/configuration.md) for which source wins. Attachment and diagnostic JSON keeps its own per-protocol list (`SensitiveJsonProperties` on each protocol's attachment options), so a name added here reaches state values and finding metadata, not those bodies.

### A walk through one test

The sample's `TheTestClockClosesTheDueWindow` was recorded with one of each layer in it. The walk below reads that trace layer by layer, including the parts the same file cannot show.

<TraceAnatomy />

### The same journey, two ways

The sample suite runs four deliberate failures next to the tests that do the same journey the right way. Pick a pair to compare what the trace recorded in each run.

<TraceDiff />

### Where in the code

Every operation your suite starts (a request, a check, a browser step, a gRPC call) records where in your code it started: the file, the line and the method, as the OpenTelemetry attributes `code.file.path`, `code.line.number` and `code.function.name`. The inspector shows that line with the code around it, so a failed check points at the line that made it.

- The location comes from the stack and your test project's symbols, which the .NET SDK writes by default. ProtoTest's own lifecycle (setup, teardown, the test's execution) records none.
- Inside a git repository the path is relative to its root, so it reads the same on every machine and does not carry your local directory layout.
- The trace embeds each source file a location points at, so the viewer can show the code without access to the repository.

```csharp
builder.ConfigureTracing(trace =>
{
    trace.CaptureSourceLocations = false; // no locations, and so no embedded code
    trace.EmbedSources = false;           // locations only, no code in the file
});
```

Turn `EmbedSources` off when a trace goes to people who should not read the suite's code. `EmbedSources` and `EmbedArtifacts` default on, so turn them both off and scope response capture (`CaptureResponses`) when the trace leaves your trust boundary.

To read a run from code instead of the viewer, see [Extending ProtoTest](../advanced/extending.md#reading-a-trace-in-code). To forward operations to an observability backend, see [OpenTelemetry](./opentelemetry.md).

`ProtoTraceDiscovery.Discover(folder)` lists the readable runs a folder holds, newest first, and names the archives it had to skip. It is the scan the `prototest` CLI and the MCP server share.

Without a browser, `ProtoTest.Traces` reads the archive and the `prototest` CLI prints the same story: [summary and index](#open-your-own-archive) cover one archive and a folder of runs. A coding agent reads the same story through the MCP server. See [Agent workflows](../agent-workflows/coding-agents.md).

## Limits

| The limit | When it matters |
| --- | --- |
| Redaction covers ProtoTest's own capture | Your own attributes and attachments can still carry application data, so treat a trace like test output |
| Untraced gaps are derived by the viewer, not stored on the wire | A gap identifies time without recorded operations; it does not diagnose what happened during that time |
| Binary-body marking detects replacement characters in recorded text | Open the attached file for its bytes; marking does not repair capture or change the archive |
| `CaptureSourceLocations` is the largest tracing cost | `EmbedSources` controls whether the archive carries the code the viewer shows; the [benchmarks page](../project/benchmarks.md) records what a trace costs at 100 and 1,000 tests and the levers that change it |
| The archive, its versions and its lifetime | [The .prototrace archive](./prototrace-archive.md): format, compatibility, run metadata and what survives a killed process |

## Learn more

- [The .prototrace archive](./prototrace-archive.md): the ZIP layout, the spans and state documents, compatibility and run metadata.
- [Coverage and observations](./coverage.md): the intentional facts the reports are built from.
- [Reporting](./reporting.md): the JSON and HTML sinks whose files travel inside the archive.
- [OpenTelemetry](./opentelemetry.md): the same operations, exported to a tracing backend.
- [Extending ProtoTest](../advanced/extending.md#reading-a-trace-in-code): reading a run from code.
