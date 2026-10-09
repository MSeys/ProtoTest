---
sidebar_position: 1
title: "ProtoTrace: see what an integration test did"
sidebar_label: ProtoTrace
description: "One portable .prototrace file records every hook, client, request, check and state change of a run, ready for the browser viewer."
---

import TraceAnatomy from '@site/src/components/TraceAnatomy';
import TraceDiff from '@site/src/components/TraceDiff';
import ViewerWalkthrough from '@site/src/components/ViewerWalkthrough';
import {Clip} from '@site/src/components/Video';

# ProtoTrace: see what an integration test did
ProtoTest records each test on its own, with no logging calls. The trace holds hooks, requests, checks, state changes, attachments and cleanup, and the run writes it to one portable `.prototrace` file. Portable has a limit: a reader only opens an archive from its own era. Check [format compatibility](./prototrace-archive.md#format-compatibility) before you archive traces long term.

<Clip name="read-a-failure" label="A pull request's trace in the viewer: the failing test, what it expected and what the page said, the source line and the page as it failed." />

When a test fails in CI, download that file and open it in the [ProtoTrace viewer](https://trace.prototest.dev). You see the failing check with the request, the response and the setup around it. Without a browser, read the same story from a terminal:

```bash
dotnet tool install --global ProtoTest.Cli
prototest summary TestResults/prototest-{runId}.prototrace
```

```text
ProtoTest trace 2.0 · run 59f2aa5ff7fd43069060b49f0cfe3106 · 2026-10-02 08:32:36Z - 2026-10-02 08:32:40Z
1 tests · 1 failed

FAILED Northstar.ProtoTest.FailureDrills.TheAddressWasHardcodedForOneMachine (2.57 s)
  ConnectionError reaching http://127.0.0.1:5099: connection refused.
  test.execution Test execution · failed
  cause: runner-reported failure
```

That is a committed failing run from the Learn track. Run ids and timestamps are new on every run, so compare the shape, not the values. [Open the sample trace](https://trace.prototest.dev/?demo=1) to look around before you have one of your own, and read [the archive format](./prototrace-archive.md#the-file-format) for what is inside.

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

With `Enabled = false`, no trace file is written and application spans from the configured sources are not captured. The in-memory recorder drops operations and records (observations, attachments, findings and tracked values). The run snapshot still lists each test with its outcome. The reports keep working, because run gates read report items, not the operation tree.

The trace is not the same thing as [observations](./coverage.md). The trace is automatic and answers "what did ProtoTest do?". Observations are facts a test chose to record. They share correlation, but observations feed the reports and the trace feeds the viewer.

## How to read it

### In the viewer

The [ProtoTrace viewer](https://trace.prototest.dev) is a static web app. Trace files are read **entirely in your browser** and never uploaded. The [sample trace](https://trace.prototest.dev/?demo=1) is a run of the sample suite, with four failing tests and one partial one.

- **The run** leads one test list with shared search and outcome filters, and splits into views:
  - **Overview** holds Needs attention, named with the diagnosis rule (Assertion, Operation error, Runner failure or Finding, in the precedence `prototest summary` uses), beside what the run could see.
  - **Timeline** places tests and their gaps on the run clock.
  - **Operations** lists the run's own work and tracked items, which open in the inspector.
  - **Details** lists the run id and every `environment.*` value, and **Files** everything the run attached.

  A view with nothing in it has no tab. `#/run/<view>` links to one.
- **A failing test** starts with a verdict bar: the diagnosis rule, the check or operation that decided it, the call it judged, and the first difference. The inspector holds the full comparison.
- **Steps** opens on the test body. Setup and teardown fold into summary rows and open along a failure. Calls carry their checks, and time with no recorded operation gets its own row.
- **Timeline** is every operation on the test clock. Zoom to one phase, search by name, kind or attribute, and filter for needs attention. Matching operations keep their ancestors for context.
- **State** shows tracked items with lifelines and changes on that same clock. Select a change to open its operation, shade its time and highlight the items it touched. Select an item to see its trail, and its operations are highlighted in Timeline.
- **Evidence** brings observations, files, findings and moments into time order. Each names the operation that recorded it, or states that none was above it.
- **The inspector** shows the source, request and response, comparisons, state changes and evidence. Moments include their attributes and sections, observations their metadata, and findings their category, tags, target and metadata. A section index jumps to each part. Binary bodies recorded as text are marked instead of shown as broken glyphs.

The **Framework** switch beside the view tabs shows, dims or hides the framework's own operations (hooks, extensions, clients, resources) in Steps and Timeline, and the machinery a test ran on in State. Dim is the default, the choice is remembered, and a framework operation that failed always stays.

The header follows the path Run > Test > Operation. Test views use `#/test/<id>/steps|timeline|state|evidence`. Old `story`, `spans` and `files` links still open the corresponding view. The URL holds the selected operation or item, including selections on the run, so a shared link keeps that place.

Hatched time means **no operation was recorded**, not that nothing happened. The viewer derives gaps inside a phase from 15% of its duration, at least 20 ms, and always from 250 ms. Test 12 in the sample has a one-second real wait, and test 15 waits for an address that cannot be reached. Both become visible gaps without changing the trace format.

These excerpts follow test 12 from the run through its four views and into the failing check:

<ViewerWalkthrough />

### Open your own archive

A run leaves its archive at `TestResults/prototest-{runId}.prototrace` under the test project's output folder, or at the path you set with `trace.OutputPath`. From there:

1. Run the suite once so the file exists.
2. Open it in the [viewer](https://trace.prototest.dev): drop the file on the page, or press **Open trace** and choose it. The trace artifact a pull request run uploaded opens the same way, as downloaded: the viewer reads the one `.prototrace` inside the ZIP.
3. Without a browser, read the same story from the terminal:

```bash
prototest summary TestResults/prototest-{runId}.prototrace
prototest index TestResults
```

`prototest summary` lists the run, the outcome counts and every test that did not fully pass, with its error, source location and the failing operation. `prototest index` writes one static `index.html` beside the runs. It holds each run's outcome counts, its failing tests, links to its trace and digest, and every archive that could not be read. A folder of evidence can then be shared without a server. Both verbs are the [ProtoTest.Cli](../agent-workflows/cli.md) tool, installed as shown at the top of this page.

### Run the viewer locally

The hosted viewer is the same static app as the repository's `viewer/` folder, so an offline or air-gapped setup runs it without the trace ever leaving the machine:

```bash
cd viewer
npm ci              # once, where the registry is reachable
npm run build       # writes the static dist/ folder
npm run preview     # serves dist/ locally
```

Then open the served page and drop the `.prototrace` archive on it, or press **Open trace** and choose it. The file stays in browser memory, as with the hosted viewer. For a closed network, build once where `npm ci` reaches the registry and carry the `dist/` folder over. It needs no server runtime, so any static file server in front of it works. `npm run dev` instead starts the development server when you work on the viewer itself.

### What a trace contains

A run contains tests, and a trace records two things about each of them:

- **What ran**: a tree of **operations**. Each has a duration and an outcome: a request, a flow, a hook. Operations nest, so a `web.flow` contains its clicks and a test's execution contains everything the body did. Moments inside an operation (a server starting, a subscription message) are **events** on it, and so are the observations, attachments and findings it produced.
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

A test can pass while something inside it failed (a diagnostic step that is not allowed to fail the run, a best-effort capture). That test is recorded as **Partial**, not Passed. A cleanup failure is not one of those steps: by default it fails the test, and the trace outcome is **Failed**. `CleanupFailures` set to `Report` restores the older reading, where that same test is **Partial** and the runner still shows it as passed. See [Lifecycle](../foundation/lifecycle.md#a-test).

Tracked values are items with kind `value` and an id of the form `{type}:{identity}`. Test-side provisioning uses the result type in snake_case as the type segment. `InvoiceLine` becomes `invoice_line`, and the item reads `invoice_line:42`. The identity is what the provisioner returned. An application's own instrumentation writes the prefix of its identity-shaped attribute instead (`invoice.id = 42` contributes `invoice:42`). The two are the same item only when the attribute prefix matches the type segment and the values match. Name a type's identity attribute after the type (`invoice_line.number`) to correlate them.

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

The in-process server is also a state entity with id `server:{type}:{application}`, and those `aspnetcore.*` attributes are its state. `{type}` is the entry point's full name, as in `server:ProtoTest.SampleApp.Program:Northstar`.

ProtoTest's own capture redacts by name:

- form fills are recorded by length
- headers and JSON properties are redacted using the [same rules as attachments](../integrations/rest/attachments.md#redaction)
- sensitive query parameter values are redacted in HTTP request URLs and web navigation addresses.

Findings, observations and attachments you record yourself are redacted only where you mark them sensitive, so treat a trace like test output.

A suite can name more values sensitive. `ConfigureRedaction` adds names to the defaults, and state values and finding metadata redact them:

```csharp
builder.ConfigureRedaction(redaction => redaction.AddSensitiveName("OwnerToken"));
```

The names travel with the host, so a second host in the same process keeps the defaults only. `ProtoTest:Redaction` binds the same names from configuration. See [Configuration](../getting-started/configuration.md) for which source wins. Attachment and diagnostic JSON keeps its own per-protocol list (`SensitiveJsonProperties` on each protocol's attachment options). A name added here reaches state values and finding metadata, not those bodies.

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

`ProtoTest.Traces` is the library that reads the archive for the [CLI](#open-your-own-archive). A coding agent reads the same story through the MCP server. See [Agent workflows](../agent-workflows/coding-agents.md).

## Limits

| The limit | When it matters |
| --- | --- |
| Redaction covers ProtoTest's own capture | Your own attributes and attachments can still carry application data, so treat a trace like test output |
| Untraced gaps are derived by the viewer, not stored on the wire | A gap identifies time without recorded operations. It does not diagnose what happened during that time. |
| Binary-body marking detects replacement characters in recorded text | Open the attached file for its bytes. Marking does not repair capture or change the archive. |
| `CaptureSourceLocations` is the largest tracing cost | `EmbedSources` controls whether the archive carries the code the viewer shows. The [benchmarks page](../project/benchmarks.md) records what a trace costs at 100 and 1,000 tests and the levers that change it. |
| The archive, its versions and its lifetime | [The .prototrace archive](./prototrace-archive.md): format, compatibility, run metadata and what survives a killed process |

## Learn more

- [The .prototrace archive](./prototrace-archive.md): the ZIP layout, the spans and state documents, compatibility and run metadata.
- [Coverage and observations](./coverage.md): the intentional facts the reports are built from.
- [Reporting](./reporting.md): the JSON and HTML sinks whose files travel inside the archive.
- [OpenTelemetry](./opentelemetry.md): the same operations, exported to a tracing backend.
- [Extending ProtoTest](../advanced/extending.md#reading-a-trace-in-code): reading a run from code.
