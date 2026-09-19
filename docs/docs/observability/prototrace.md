---
sidebar_position: 1
title: ProtoTrace
description: "ProtoTrace records every hook, client, request, check and state change of a run into one portable file, readable in the browser-based viewer."
---

# ProtoTrace

ProtoTrace is ProtoTest's execution trace. Because ProtoTest coordinates the lifecycle and understands its integrations, it can record what happened in every test — hooks, attributes, clients, state, requests, browser actions, assertions, attachments, cleanup — **without a single logging line in your tests**. At the end of the run, everything is written to one portable `.prototrace` file.

When a test fails in CI, you download that file and open it in the [ProtoTrace viewer](https://trace.prototest.dev). You see the failing assertion *in context*: which user was set up, what the request looked like, what came back, what the browser showed.

## It's on by default

```csharp
builder.ConfigureTracing(trace =>
{
    trace.Enabled = true;                                       // default
    trace.OutputPath = "TestResults/billing.prototrace";        // default: TestResults/prototest-{runId}.prototrace
});
```

With `Enabled = false`, nothing is recorded and no file is written. Observations and reports keep working.

## What a trace contains

A run contains tests, and a trace records two things about each of them:

- **What ran** — a tree of **operations** (spans): each has a duration and an outcome — a request, a flow, a hook. Operations nest: a `web.flow` contains its clicks, a test's execution contains everything the test body did. Moments inside an operation — a server starting, a subscription message — are **events** on it, and so are the observations, attachments and findings it produced.
- **What existed and changed** — the **state**: every client, context, resource and tracked value, with its state at the end and a trail of changes. Each change names the operation that caused it, and where the value came from: the test itself, a response it observed, or the application's own instrumentation.

Every entry belongs to a **phase**:

| Phase | |
| --- | --- |
| `Setup` | hooks and attributes before the test body |
| `Execution` | the test body |
| `Teardown` | hooks, attributes and disposal afterwards |
| `Rollback` | teardown after a *failed* setup |
| `Run` | run-level work |

…and ends with an **outcome**: `Succeeded`, `Failed`, `Partial`, `Cancelled`, `Skipped` or `Unknown`.

### Partial

A test can pass while something inside it failed — a diagnostic step that isn't allowed to fail the run, a best-effort capture. That test is recorded as **Partial** rather than green, so it doesn't hide in a sea of passing tests.

### Entries you'll see

A few of the kinds recorded automatically:

| Kind | From |
| --- | --- |
| `test.setup`, `test.execution`, `test.teardown`, `test.rollback` | the lifecycle |
| `client.initialize`, `client.resolve`, `client.register` | clients |
| `context.set`, `context.resolve` | typed state — with a snapshot of the value |
| `observation.record`, `attachment.register` | observations and attachments |
| `auth.outcome` (`applied` / `skipped`) | HTTP authentication, recorded on the request operation itself |
| `auth.handler.apply` | each handler of a composite authenticator |
| `assert.json.shape` | shape assertions — expected, actual and matched properties |
| `web.navigate`, `web.click`, `web.flow`, `web.login`, `assert.web`, … | the [browser](../integrations/web/diagnostics.md#what-the-trace-records-for-every-operation) |
| `data.build`, `data.build_many`, `data.create`, `data.create_many`, `data.explain` | [building test data](../integrations/data/index.md) |
| `data.provision`, `data.cleanup`, `data.value.resolve` | [provisioning and cleanup](../integrations/data/provisioners.md) |
| `aspnetcore.server.initialize` | the in-process server |

Sensitive values stay out: form fills are recorded by length, headers and JSON properties are redacted using the [same rules as attachments](../integrations/rest/attachments.md#redaction), and sensitive query parameter values are redacted in HTTP request URLs and web navigation addresses.

## Trace vs. observations

They look similar and answer different questions:

- **Trace** — automatic. *What did ProtoTest do?*
- **[Observations](./coverage.md)** — intentional. *What did the test learn?* Coverage facts, measurements, findings.

They share correlation, but they're kept separate: observations feed reports, the trace feeds the viewer.

## Viewing a trace

The [ProtoTrace viewer](https://trace.prototest.dev) is a static web app. Trace files are read **entirely in your browser** and never uploaded. To look around before you have a trace of your own, [open the sample trace](https://trace.prototest.dev/?demo=1): a run of the demo suite, with a failing test and two partial ones.

- **The run** opens with its verdict, what needs attention — failing and partial tests with the check that decided them, findings, gates — and what the run could see: where the application ran, which capabilities were composed, and which sources of values were present.
- **A failing test** leads with its failure: the check that failed, expected against actual for every property, and the call it judged.
- **Story** tells the test phase by phase: each call carries its checks, and the framework's own steps fold away until you open them.
- **State** shows every tracked item with its lifeline and changes; select a change to jump to the operation that made it.
- **Spans** is the complete, searchable tree.
- **The inspector** shows everything one operation recorded — where in your code it started, request and response, JSON as a collapsible tree, the shape a check validated, what it changed — and every item's change trail. The address holds the selection, so a link opens the same place.

The viewer's source is in the repository under [`viewer/`](https://github.com/MSeys/ProtoTest/tree/main/viewer) if you'd rather host it yourself.

## Where in the code

Every operation your suite starts — a request, a check, a browser step, a gRPC call — records where in your code it started: the file, the line and the method, as the OpenTelemetry attributes `code.file.path`, `code.line.number` and `code.function.name`. The inspector shows that line with the code around it, so a failed check points at the line that made it.

- The location comes from the stack and your test project's symbols, which the .NET SDK writes by default. ProtoTest's own lifecycle — setup, teardown, the test's execution — records none.
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

## The file format

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

- **`spans.json`** holds a resource group per test — its id, name, class, method, outcome and duration — with its operations, their events, and the artifacts the test declared. The run's own group carries its id, start and end, and the environment it ran in (`environment.runtime`, `environment.os`, …); run-level events such as gate verdicts sit on it too.
- **`state.json`** holds the run's tracked items and each test's: kind, id, name, scope, first and last seen, the state at the end and every change, with the operation that caused it.
- **`sources`** in the manifest maps each recorded `code.file.path` to its embedded copy.
- Each artifact is declared once, with its media type, size and path in the archive; an attachment event refers to it by id.

Entries are stored **uncompressed**, so a browser can read the archive without a decompression library. Property names are camelCase. Traces written before format 2.0 had a single `run.json` instead; the current viewer says so and asks for a trace from a current ProtoTest.

Test artifacts — every [attachment](../foundation/attachments.md) — live under the test's id. Run-level artifacts, such as the reports written by [sinks](./reporting.md), live under `resources/run/`.

## Reading a trace in code

The host exposes a live snapshot, which is how ProtoTest's own tests assert on tracing:

```csharp
var run = host.Trace.Snapshot();
var click = run.Tests.Single().Entries.Single(entry => entry.Kind == "web.click");
Assert.That(click.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
```

To add your own entries, see [Extending ProtoTest](../advanced/extending.md#adding-to-the-trace). To forward operations to an observability backend, see [OpenTelemetry](./opentelemetry.md).
