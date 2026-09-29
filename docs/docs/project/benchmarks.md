---
sidebar_position: 3
title: Benchmarks
description: "Measured trace size, run time, export time and memory growth for synthetic suites, and the levers that change them."
---

# Benchmarks

These numbers come from the in-repo harnesses - `tests/ProtoTest.Core.Tests/TraceScaleTests.cs` for trace size and scale, `tests/ProtoTest.Core.Tests/PerTestPhaseBenchmarkTests.cs` for the per-test phase profile, `tests/ProtoTest.AspNetCore.Tests/OverheadBenchmarkTests.cs` for the `WebApplicationFactory` comparison, and `tests/ProtoTest.Messaging.RabbitMq.Tests/RabbitMqTests.cs` (`PerTestTapLifecycle_ShouldStayWithinTheSanityBound`) for the broker tap cost - on an AMD Ryzen 7 9800X3D (8 cores, 16 threads), Windows 11, .NET 8 (the test projects' target). The OpenCSMS numbers come from that repository's own `eng/run-benchmark.ps1`. They are indicative, not a contract: run the harnesses on your own hardware and CI image before quoting them, and expect run-to-run medians to move by around 20%.

| Tests | Trace size | Run time | Stop + export | Allocated | Peak working set growth |
| --- | --- | --- | --- | --- | --- |
| 100 | 0.70 MB | 78 ms | 49 ms | 38 MB | 44 MB |
| 1,000 | 6.95 MB | 389 ms | 59 ms | 377 MB | 23 MB |

Every synthetic test records one operation, one event, one entity-state change and one observation - roughly the evidence a small API test produces. That is about 7 KB of trace and about 0.4 MB of allocation per test at this size. The harness runs with `EmbedSources = false` and `EmbedArtifacts = false`; the defaults (embedding the suite's source files and attachment bytes) produce a larger file.

## A short test, written twice

The comparison an evaluator actually cares about: one short test - POST an order, read it back, check both
bodies - written twice against the same in-process application and measured a whole test at a time. The raw
side uses `WebApplicationFactory` with `System.Net.Http.Json`, creates a client per test and deserializes with
`JsonSerializer`; the ProtoTest side runs the full lifecycle and asserts with
`Should.HaveHttpStatus(...).Should.MatchShape(...)`. Same requests, same assertions, 32 warmups and 256
measured, medians:

| Mode | Per test | p95 | Start | Call | Complete | Allocated |
| --- | --- | --- | --- | --- | --- | --- |
| ProtoTest, tracing on | 2.55 ms | 3.79 ms | 0.66 ms | 1.23 ms | 0.55 ms | 1,354 KB |
| ProtoTest, tracing off | 0.95 ms | 1.29 ms | 0.10 ms | 0.79 ms | 0.05 ms | 327 KB |
| Raw stack (the same test) | 0.33 ms | 0.47 ms | 0.01 ms | 0.32 ms | - | 66 KB |

Read plainly: the same test costs about **3x the raw stack without tracing and about 8x with it**. The trace
adds roughly 1.6 ms and 1 MB per test here, because the short test produces two recorded calls, their
observations and the shape validations. A 1,000-test suite pays about 2.6 s for the framework and the trace,
against 0.3 s raw - on a suite whose tests talk to a database or a browser, that difference disappears into
the setup the framework is replacing.

## Where the difference comes from

A micro comparison explains the number above: a single request (`GET /ping`), a bare lifecycle with no
request at all, and the raw equivalent - 32 warmups and 256 measured, medians:

| Mode | Per test | p95 | Start | Call | Complete | Allocated |
| --- | --- | --- | --- | --- | --- | --- |
| ProtoTest, tracing on | 1.54 ms | 2.26 ms | 0.62 ms | 0.32 ms | 0.54 ms | 1,003 KB |
| ProtoTest, tracing off | 0.34 ms | 0.50 ms | 0.09 ms | 0.21 ms | 0.04 ms | 171 KB |
| ProtoTest, lifecycle only (no request) | 0.06 ms | 0.15 ms | 0.04 ms | - | 0.02 ms | 60 KB |
| Raw `WebApplicationFactory` | 0.05 ms | 0.16 ms | 0.00 ms | 0.04 ms | - | 17 KB |

Suite startup, measured through the first completed request so both sides pay for building the in-process
server: ProtoTest 22 ms with tracing, 13 ms without, raw 12 ms.

- **The lifecycle itself is cheap.** Start and complete a test with no request at all: 0.06 ms and 60 KB -
  about the raw baseline's whole per-test work. "Tracing off" is not "framework off": `Enabled = false`
  skips the activity listener, the recorder and trace export, but the context, hook pipeline, per-test
  client creation and outcome release still run.
- **The REST client adds about 0.15 ms over a raw request** (0.20 vs 0.05 ms). It reads and captures the
  response body - the raw baseline leaves it unread until asked - records the response observation and
  entity state, and builds the typed wrapper. A raw test that asserts a body pays part of that difference
  itself.
- **The trace is the remaining cost.** Tracing adds roughly 1.0 ms and 0.7 MB per test over the same suite
  with tracing off (start +0.5 ms of setup, complete +0.5 ms of recording and finalizing, call +0.1 ms).
- **Startup is nearly identical** (22 vs 12 ms): booting the application dominates, not the framework.
- **The raw baseline creates an `HttpClient` per test**, matching ProtoTest's per-test model; a raw suite
  that reuses one client will be faster than the numbers shown.
- A real test does much more than one request. When a test starts containers, drives a browser or seeds a
  database, these milliseconds are noise; this table is the cost of the framework's own bookkeeping, not of
  a typical test.

## The per-test phase profile

One lifecycle broken into the phases the framework owns, on a bare host with no application, 64 warmups
and 512 measured iterations per phase, medians. The host runs with `EmbedSources = false` and
`EmbedArtifacts = false` and source locations on (the default), the same trace settings the OpenCSMS
benchmark uses; the harness is `tests/ProtoTest.Core.Tests/PerTestPhaseBenchmarkTests.cs`.

| Phase | Median | p95 | Allocated |
| --- | --- | --- | --- |
| DI scope, create + dispose | 0.0001 ms | 0.0002 ms | 0.2 KB |
| Context create | 0.0027 ms | 0.0034 ms | 6.8 KB |
| Context dispose (resources + scope) | 0.0306 ms | 0.0350 ms | 29.3 KB |
| Per-test clock register | 0.0002 ms | 0.0020 ms | 1.2 KB |
| Attribute + skip resolution | 0.0005 ms | 0.0007 ms | 0.6 KB |
| Attribute + skip resolution, one `[Requires...]` | 0.0017 ms | 0.0020 ms | 1.6 KB |
| Trace recorder, tracing off, 5 operations | 0.0021 ms | 0.0029 ms | 4.8 KB |
| Trace recorder, tracing on, 5 operations | 0.1077 ms | 0.1388 ms | 136.8 KB |
| Trace recorder, tracing on, source locations off | 0.0027 ms | 0.0031 ms | 8.6 KB |
| Lifecycle, tracing off (start + complete) | 0.0154 ms | 0.0162 ms | 20.6 KB |
| Lifecycle, tracing on | 0.3302 ms | 0.5840 ms | 374.8 KB |
| Lifecycle, tracing on, source locations off | 0.0200 ms | 0.0205 ms | 29.5 KB |
| Lifecycle, tracing on, 5 more operations | 0.4910 ms | 0.7293 ms | 519.8 KB |
| Lifecycle, tracing on, 1 declared attachment | 0.3782 ms | 0.6423 ms | 377.8 KB |

Read plainly: the framework's own work is microseconds. DI scope creation, context creation, per-test
clock registration, attribute and skip resolution and teardown all stay under 0.04 ms. Tracing costs about
0.33 ms and 375 KB per test, and **source-location capture is essentially all of it**: with
`CaptureSourceLocations = false` the same lifecycle costs 0.02 ms and 30 KB.

## OpenCSMS at 1,000 tests

The OpenCSMS repository's `eng/run-benchmark.ps1` runs the product itself - API, billing worker, real
PostgreSQL and RabbitMQ from the environment - through one health-check test cycle per iteration (1,000
iterations, 100 warmups), against a raw `WebApplicationFactory` baseline, plus a 1,000-test seeded journey
run. Same machine and .NET 8. The local ProtoTest feed is rebuilt for each round.

The showpiece trace is [opencsms-showpiece.prototrace](/traces/opencsms-showpiece.prototrace): the
`IdleFeeAfterTariffChange` journey recorded before the tariff-snapshot fix, when a reprice during an open
session changed the session's billing. The fixed journey asserts the session's original tariff and runs in
the OpenCSMS CI.

| Mode | Run | Per test | Start | Call | Complete |
| --- | --- | --- | --- | --- | --- |
| ProtoTest, tracing on | M7.1 published | 43.53 ms | 15.87 ms | 6.17 ms | 21.52 ms |
| ProtoTest, tracing on | release pass, before | 42.10 ms | 15.97 ms | 5.83 ms | 20.11 ms |
| ProtoTest, tracing on | release pass, after | 41.04 - 42.98 ms | 18.04 - 18.19 ms | 5.67 - 5.70 ms | 17.18 - 18.72 ms |
| ProtoTest, tracing on | prepare pass, before | 44.38 ms | 17.87 ms | 5.73 ms | 20.04 ms |
| ProtoTest, tracing on | prepare pass, after | 35.15 - 36.17 ms | 11.95 - 12.09 ms | 5.69 - 5.72 ms | 17.36 - 17.87 ms |
| ProtoTest, tracing off | release pass, before | 45.66 ms | 15.45 ms | 5.83 ms | 24.40 ms |
| ProtoTest, tracing off | release pass, after | 55.64 - 56.23 ms | 17.62 - 17.67 ms | 5.69 - 5.71 ms | 32.18 - 32.74 ms |
| ProtoTest, tracing off | prepare pass, before | 52.98 ms | 17.38 ms | 5.74 ms | 29.55 ms |
| ProtoTest, tracing off | prepare pass, after | 30.74 - 32.40 ms | 11.14 - 11.65 ms | 5.55 - 5.57 ms | 13.67 - 15.91 ms |
| Raw `WebApplicationFactory` | every pass | 4.79 - 5.00 ms | 0.00 ms | 4.79 - 5.00 ms | - |

Suite startup through the first completed request landed between 156 and 179 ms with tracing, 154 and 176 ms without, and 73 and 83 ms raw across the passes. The 1,000-test health-check trace is 25.8 MB (about 25 KB per test); the
1,000-journey trace is 38.8 MB, and the journey median moved from 67.9 ms before the release fix (64.4 to
69.5 ms after) to 63.9 ms before the prepare fix and 49.3 and 54.8 ms after it.

Read the table honestly: the per-test total moves inside the run-to-run band, and the tracing-off legs are
the most order-sensitive. They run second in the harness, after the first leg has created and deleted about
3,300 tap queues on the shared broker; the with-tracing legs of the same runs show the changed path, and the
controlled comparison below isolates it.

The run's own trace explains where the milliseconds are, and they are not the framework. Medians per
operation over the 1,100 health-check tests of the recorded traces:

| Operation | Release pass, before / after | Prepare pass, before / after | What it is |
| --- | --- | --- | --- |
| `client.initialize` for the messaging client | 14.86 / 17.29 ms | 17.11 / 11.09 - 11.26 ms | the test's own consumer, prepared for the suite's three tap destinations |
| `resource.release` for that consumer | 20.99 / 16.66 ms | 18.99 / 17.00 - 17.49 ms | its tap queues and channels released |
| `http.request` (`GET /healthz`) | 6.14 / 5.63 ms | 5.70 / 5.65 - 5.69 ms | the request itself, through the REST client |
| Everything else | under 1 ms | under 1 ms | HTTP client init, hooks, attributes, execution and teardown scaffold, trace included |

The release dropped by about 4 ms, and the preparation drifted up by about 2 ms with the broker state, so
the health-check total stayed flat in the release pass. The prepare pass moved the preparation instead:
17.11 ms to 11.09-11.26 ms, with the release and the request steady. The controlled A/B is the honest
number: three tapped destinations against the same host and broker with none, several runs before and after
each fix in one session, the no-destination host at 0.03 ms
(`PerTestTapLifecycle_ShouldStayWithinTheSanityBound` in the RabbitMQ suite). The release fix's
complete-phase medians were 17.25, 17.19 and 16.84 ms before and 13.04, 16.17 and 12.40 ms after; the
prepare fix's start-phase medians were 15.91, 16.19 and 16.63 ms before and 12.41, 11.95, 10.60, 10.70 and
10.67 ms after (the first run after a rebuild measured 18.41 ms and is left out).

So about 30 of the 36 ms is the suite's per-test broker isolation, about 6 ms is the request, and under
1 ms is ProtoTest. The raw baseline never opens a broker consumer; a test that never touches messaging
still pays the tap cost, because `Tap` promises the destination is bound before the test acts.

- **"Tracing off" works; it is just not the cost.** `Enabled = false` installs no activity listener, the
  recorder drops operations and records, and no archive is written (pinned by
  `ProtoEvidenceBoundaryTests.DisabledTracing_ShouldNotRecordOperationsOrWriteAnArchive`). The benchmark
  numbers move within noise because the trace is about 0.33 ms of a 36 ms cycle, and the phase profile
  above shows where the 0.33 ms sits: source-location capture.
- **Preparation is the floor.** Each tap costs five broker round trips (channel, queue declare, two
  bindings, consume), and the taps are prepared concurrently, each on its own channel, so their round trips
  overlap: the controlled A/B above moves three taps' prepare phase from about 16 ms to about 11-12 ms.
  Every destination is still attempted, so one that cannot be prepared fails only the await that names it.

## The viewer at 1,000+ tests

The viewer was measured on a generated 1,200-test trace derived from the committed demo trace:
`viewer/scripts/generate-scale-trace.mjs` clones each demo test until the requested count, rewriting ids,
names and timestamps, and writes small placeholder payloads for the artifacts. Every test keeps the demo's
real operations, checks, observations, sections, events and state, so the viewer exercises its real paths.
The generated file is 8.6 MB zipped (57.8 MB of spans JSON, 35.6 MB of state JSON); it is a heavier suite
per test than the size table above, which counts one operation per test.

Method: a production build (`npm run build`), Microsoft Edge 154.0.4258.37 headless at 1440x900, no CPU or
network throttling, on the same Ryzen 7 9800X3D machine. Each number is the median of five cold runs: a fresh page,
the trace loaded through the viewer's own file input, then one interaction. The harness is
`viewer/scripts/measure-viewer.mjs`; it serves the built `dist` over loopback, so no dev server is involved.
Before and after the pass:

| Step | Before | After |
| --- | --- | --- |
| Open the trace and render the run list | 1,356 ms | 1,316 ms |
| Filter to "Needs attention" | 77 ms | 53 ms |
| Clear the filter (1,200 rows return) | 182 ms | 162 ms |
| Type "GraphQL" in the run search | 365 ms | 231 ms |
| Open a test with 169 spans | 167 ms | 151 ms |
| Return to the run list | 650 ms | 566 ms |

Read plainly: a cold open of a suite this size takes about **1.3 seconds**, and the remaining run list
steps land between **50 ms and 570 ms**. The metric views stay fast at this size: the spans tab, the state
view and the inspector all answer in 33 to 50 ms for a 169-span test.

What the pass changed, all inside the existing views:

- Each test computes its display name, title, group and search text once and caches it, instead of
  re-deriving them for every row on every render.
- The run view is kept alive when a test opens, so returning moves the cached DOM instead of rebuilding
  a thousand rows.
- Each run and rail row contains its own layout and paint (`contain: layout paint`), so one row's change
  never re-lays-out the list.

What remains, with the measurement that shows it:

- The cold open is dominated by reading and parsing the two JSON documents and the first full layout
  (five long tasks, the longest about 550 ms). Removing that needs a streaming reader.
- The run list and the outcome strip render one element per test, so returning to the run and changing
  the filter move about a thousand DOM rows. Removing that needs a windowed list.
- `content-visibility: auto` on the rows was measured and rejected: it skipped off-screen layout on the
  first render but made typed filtering about twice as slow, because every row added or removed during
  filtering pays its bookkeeping.

## Levers

| Lever | Effect |
| --- | --- |
| `trace.CaptureSourceLocations = false` | Drops the `code.file.path` / `code.line.number` / `code.function.name` attributes; this is essentially the whole trace cost (0.32 ms -> 0.02 ms and 375 KB -> 30 KB per bare test in the phase profile) |
| `trace.EmbedSources = false` | Keeps source locations but stops embedding the files they point at |
| `trace.EmbedArtifacts = false` | Declares attachments (name, media type, size) without reading or writing their bytes |
| `trace.MaxArtifactBytes` | Caps any single artifact; an over-limit attachment becomes an error artifact (default 64 MB) |
| Attachment capture per integration | Request/response/screenshot/trace capture is opt-in (`CaptureAttachments(...)`); leaving it off is the biggest lever |

## Re-running

```
dotnet test tests/ProtoTest.Core.Tests --filter TraceScaleTests
```

The harness prints `[scale] tests=... trace=... run=... stop+export=... allocated=...` for each size. Its assertions are sanity bounds - a trace must stay proportional to the test count - not performance targets.

The overhead comparison re-runs with:

```
dotnet test tests/ProtoTest.AspNetCore.Tests --filter Category=Benchmark --logger "console;verbosity=detailed"
```

It prints `[overhead]` lines per mode - the short test written both ways, the single-request comparison, the bare lifecycle and each side's startup - and its assertions are generous sanity bounds too, not targets.

The per-test phase profile re-runs with:

```
dotnet test tests/ProtoTest.Core.Tests --filter PerTestPhaseBenchmarkTests --logger "console;verbosity=detailed"
```

The broker tap comparison re-runs with (a broker from `ProtoTest__Messaging__RabbitMq__ConnectionString` or a container runtime):

```
dotnet test tests/ProtoTest.Messaging.RabbitMq.Tests --filter PerTestTapLifecycle --logger "console;verbosity=detailed"
```

The viewer numbers re-run from the `viewer` directory:

```
npm run scale:trace
npm run build
npm run scale:measure -- --trace .perf/scale-1200.prototrace --tests 1200 --runs 5
```

The generator prints the trace's size, the harness prints every run and its median, and `--profile` also
writes CPU profiles and renderer time (script, layout, style) per measured step. Neither script is part of
the build or CI; the harness needs an installed Chromium-family browser (Edge by default) and
`puppeteer-core`, which is a viewer dev dependency.

## Parallel execution

The `ProtoTest.Demo` sample (since retired, containers and real browsers included) was stable at 8 and 32 workers on a 16-core machine and
showed one non-reproducible failure at 64 workers, recorded 2026-09-24. The [concurrency page](../foundation/concurrency.md#exercised-parallelism)
records the runs and the failure mode.
