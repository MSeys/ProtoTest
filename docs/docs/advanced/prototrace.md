---
sidebar_position: 1
title: ProtoTrace
---

# ProtoTrace

ProtoTrace is ProtoTest's execution trace. Because ProtoTest owns the lifecycle and understands its integrations, it can record what happened in every test — hooks, attributes, clients, state, requests, browser actions, assertions, attachments, cleanup — **without a single logging line in your tests**. At the end of the run, everything is written to one portable `.prototrace` file.

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

A run contains tests; a test contains a **tree of entries**.

Each entry is either an **operation** (it has a duration and an outcome — a request, a flow, a hook) or an **event** (a moment — a state change, a console message). Operations nest: a `web.flow` contains its clicks, a test's execution contains everything the test body did.

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
| `auth.apply`, `auth.skip` | HTTP authentication |
| `assert.json.shape` | shape assertions — expected, actual and matched properties |
| `web.navigate`, `web.click`, `web.flow`, `web.login`, `assert.web`, … | the [browser](../integrations/web/diagnostics.md#what-the-trace-records-for-every-operation) |
| `data.build`, `data.cleanup` | [test data](../integrations/data/index.md) |
| `aspnetcore.server.initialize` | the in-process server |

Sensitive values stay out: form fills are recorded by length, and headers, query parameters and JSON properties are redacted using the [same rules as attachments](../integrations/rest/attachments.md#redaction).

## Trace vs. observations

They look similar and answer different questions:

- **Trace** — automatic. *What did ProtoTest do?*
- **[Observations](./coverage.md)** — intentional. *What did the test learn?* Coverage facts, measurements, findings.

They share correlation, but they're kept separate: observations feed reports, the trace feeds the viewer.

## Viewing a trace

The [ProtoTrace viewer](https://trace.prototest.dev) is a static web app. Trace files are read **entirely in your browser** and never uploaded.

- Tests are grouped by class, with pass/fail/partial at a glance.
- Each test has tabs for its overview, lifecycle, the full operation tree, network traffic, assertions, observations, artifacts and errors.
- Parallel tests appear as separate lanes on the run timeline, so overlap is visible.
- The inspector walks ancestors and descendants — from a failed assertion up to the request that produced it and the setup that preceded it.
- Shape mismatches and JSON payloads render as expandable structured data.

The viewer's source is in the repository under [`viewer/`](https://github.com/MSeys/ProtoTest/tree/main/viewer) if you'd rather host it yourself.

## The file format

A `.prototrace` file is a ZIP archive:

```
run.prototrace
├── manifest.json            { "formatVersion": "1.2", "runEntry": "run.json" }
├── run.json                 the whole run: tests, entries, artifacts, environment
└── resources/
    ├── {testId}/artifact-1/rest-01-response.json
    ├── {testId}/artifact-2/playwright-default-trace.zip
    └── run/HtmlReportSink/run-artifact-1/report.html
```

Entries are stored **uncompressed**, so a browser can read the archive without a decompression library. `run.json` uses camelCase property names and string enums, and records the runtime, OS and architectures in `environment`.

Test artifacts — every [attachment](../foundation/attachments.md) — live under the test's id. Run-level artifacts, such as the reports written by [sinks](./reporting.md), live under `resources/run/`.

## Reading a trace in code

The host exposes a live snapshot, which is how ProtoTest's own tests assert on tracing:

```csharp
var run = host.Trace.Snapshot();
var click = run.Tests.Single().Entries.Single(entry => entry.Kind == "web.click");
Assert.That(click.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
```

To add your own entries, see [Extending ProtoTest](./extending.md#adding-to-the-trace). To forward operations to an observability backend, see [OpenTelemetry](./opentelemetry.md).
