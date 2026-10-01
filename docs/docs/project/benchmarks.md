---
sidebar_position: 3
title: Benchmarks
description: "Measured trace size, run time, export time and memory growth for synthetic suites, and the levers that change them."
---

# Benchmarks

This page answers two questions: what ProtoTest adds to a test's run time, and how big its trace gets. The
numbers come from harnesses in this repository, run on one machine: an AMD Ryzen 7 9800X3D, Windows 11 and .NET 8.
They are indicative, not a contract. Expect run-to-run medians to move by around 20%, and re-run the harnesses
on your own hardware before you quote them.

## What a test costs

One short test, written twice against the same in-process application: POST an order, read it back, check both
bodies. **Neither side starts a container or a broker.** The raw side uses `WebApplicationFactory` with
`System.Net.Http.Json`, creates a client per test and deserializes with `JsonSerializer`. The ProtoTest side runs
the full lifecycle and asserts with `Should.HaveHttpStatus(...).Should.MatchShape(...)`. Same requests, same
assertions, 32 warmups and 256 measured, medians:

| Mode | Per test | p95 | Start | Call | Complete | Allocated |
| --- | --- | --- | --- | --- | --- | --- |
| ProtoTest, tracing on | 2.55 ms | 3.79 ms | 0.66 ms | 1.23 ms | 0.55 ms | 1,354 KB |
| ProtoTest, tracing off | 0.95 ms | 1.29 ms | 0.10 ms | 0.79 ms | 0.05 ms | 327 KB |
| Raw stack (the same test) | 0.33 ms | 0.47 ms | 0.01 ms | 0.32 ms | - | 66 KB |

ProtoTest adds about 0.6 ms per test without tracing and 2.2 ms with it. Because the test itself does almost
nothing, that reads as about **3x and 8x the raw stack**. That ratio is the worst case. A test that seeds a
database, waits for a message or drives a browser spends milliseconds to seconds on that work, and the same
fixed cost shrinks into it. A 1,000-test suite of tests this small pays about 2.6 s for the framework and the
trace, against 0.3 s raw.

Almost all of the tracing cost is **source-location capture**. Setting `CaptureSourceLocations = false` takes a
bare test's trace cost from 0.33 ms to 0.02 ms, as [the phase profile](#for-contributors-the-details-behind-the-numbers)
shows. Suite startup, through the first completed request, is 22 ms with tracing, 13 ms without and 12 ms raw.

## Where the time goes in a real suite \{#opencsms-at-1000-tests}

The [OpenCSMS](https://github.com/MSeys/OpenCsms) suite runs the product itself: the API, the billing worker,
and real PostgreSQL and RabbitMQ. Its `eng/run-benchmark.ps1` times a health-check test, 1,000 iterations after
100 warmups, on the same machine. The run's own trace shows where each test's 36 ms goes:

| Operation | Median | What it is |
| --- | --- | --- |
| `client.initialize` for the messaging client | 11.09 - 11.26 ms | the test's own consumer, prepared for the suite's three tap destinations |
| `resource.release` for that consumer | 17.00 - 17.49 ms | its tap queues and channels released |
| `http.request` (`GET /healthz`) | 5.65 - 5.69 ms | the request itself, through the REST client |
| Everything else | under 1 ms | HTTP client init, hooks, attributes, execution and teardown scaffold, trace included |

So about 30 of the 36 ms is the suite's **per-test broker isolation**: each test gets its own consumer, bound to
three tap destinations before it acts, and released afterwards. About 6 ms is the request. Under 1 ms is
ProtoTest. A test that never touches messaging still pays the tap cost in this suite, because `Tap` promises the
destination is bound before the test acts. Each tap costs five broker round trips (channel, queue declare, two
bindings, consume), and the taps are prepared concurrently.

The same run also measured a raw `WebApplicationFactory` baseline. **It is not a like-for-like comparison:** the
raw side calls the health check but opens no broker consumer, so it skips the 30 ms of isolation the ProtoTest
suite chose to have.

| Mode | Per test | Start | Call | Complete |
| --- | --- | --- | --- | --- |
| ProtoTest, tracing on | 35.15 - 36.17 ms | 11.95 - 12.09 ms | 5.69 - 5.72 ms | 17.36 - 17.87 ms |
| ProtoTest, tracing off | 30.74 - 32.40 ms | 11.14 - 11.65 ms | 5.55 - 5.57 ms | 13.67 - 15.91 ms |
| Raw `WebApplicationFactory` | 4.79 - 5.00 ms | 0.00 ms | 4.79 - 5.00 ms | - |

Suite startup through the first completed request is 156 to 179 ms with tracing, 154 to 176 ms without, and 73
to 83 ms raw, for the same reason. Turning tracing off moves the total within noise, because the trace is about
0.33 ms of a 36 ms cycle.

The showpiece trace is [opencsms-showpiece.prototrace](/traces/opencsms-showpiece.prototrace): the
`IdleFeeAfterTariffChange` journey as it failed, when a reprice during an open session changed the session's
billing.

## How big a trace gets

Each synthetic test below records one operation, one event, one entity-state change and one observation, roughly
what a small API test produces. The harness runs with `EmbedSources = false` and `EmbedArtifacts = false`, so a
default run, which embeds source files and attachment bytes, writes a larger file.

| Tests | Trace size | Run time | Stop + export | Allocated | Peak working set growth |
| --- | --- | --- | --- | --- | --- |
| 100 | 0.70 MB | 78 ms | 49 ms | 38 MB | 44 MB |
| 1,000 | 6.95 MB | 389 ms | 59 ms | 377 MB | 23 MB |

That is about 7 KB of trace per test. The OpenCSMS health-check trace is 25.8 MB for 1,000 tests, about 25 KB
each, and its 1,000-journey trace is 38.8 MB. The viewer opens a 1,200-test trace in about 1.3 seconds.

## Levers

| Lever | Effect |
| --- | --- |
| `trace.CaptureSourceLocations = false` | Drops the `code.file.path` / `code.line.number` / `code.function.name` attributes. This is almost the whole trace cost: 0.33 ms -> 0.02 ms and 375 KB -> 30 KB per bare test in the phase profile. |
| `trace.EmbedSources = false` | Keeps source locations but stops embedding the files they point at |
| `trace.EmbedArtifacts = false` | Declares attachments (name, media type, size) without reading or writing their bytes |
| `trace.MaxArtifactBytes` | Caps any single artifact, 64 MB by default. An over-limit attachment becomes an error artifact. |
| Attachment capture per integration | Request, response, screenshot and trace capture is opt-in (`CaptureAttachments(...)`). Leaving it off is the biggest lever. |

## For contributors: the details behind the numbers

<details>
<summary>The harnesses, the per-request breakdown and the phase profile</summary>

| Question | Harness |
| --- | --- |
| How big is the trace, and how does it scale | `tests/ProtoTest.Core.Tests/TraceScaleTests.cs` |
| What does one test cost, phase by phase | `tests/ProtoTest.Core.Tests/PerTestPhaseBenchmarkTests.cs` |
| What does the framework add over raw `WebApplicationFactory` | `tests/ProtoTest.AspNetCore.Tests/OverheadBenchmarkTests.cs` |
| What does a per-test broker tap cost | `tests/ProtoTest.Messaging.RabbitMq.Tests/RabbitMqTests.cs` (`PerTestTapLifecycle_ShouldStayWithinTheSanityBound`) |

A single request (`GET /ping`), a bare lifecycle with no request, and the raw equivalent, 32 warmups and 256
measured, medians:

| Mode | Per test | p95 | Start | Call | Complete | Allocated |
| --- | --- | --- | --- | --- | --- | --- |
| ProtoTest, tracing on | 1.54 ms | 2.26 ms | 0.62 ms | 0.32 ms | 0.54 ms | 1,003 KB |
| ProtoTest, tracing off | 0.34 ms | 0.50 ms | 0.09 ms | 0.21 ms | 0.04 ms | 171 KB |
| ProtoTest, lifecycle only (no request) | 0.06 ms | 0.15 ms | 0.04 ms | - | 0.02 ms | 60 KB |
| Raw `WebApplicationFactory` | 0.05 ms | 0.16 ms | 0.00 ms | 0.04 ms | - | 17 KB |

```text
raw baseline        0.05 ms
lifecycle only      0.06 ms   framework bookkeeping is nearly free
+ REST client       0.34 ms   +0.28 ms: the request, capture, wrapping and body reads
+ trace             1.54 ms   +1.2 ms of source locations and recording
```

- "Tracing off" is not "framework off". `Enabled = false` skips the activity listener, the recorder and trace
  export, but the context, hook pipeline, per-test client creation and outcome release still run. With no
  request at all, the lifecycle costs 0.06 ms and 60 KB.
- The REST client adds about 0.17 ms to the call itself (0.21 vs 0.04 ms in the Call column). It reads and
  captures the response body, which the raw baseline leaves unread until asked. It also records the response
  observation and entity state, and builds the typed wrapper.
- Tracing adds roughly 1.2 ms and 0.8 MB per request: start +0.5 ms, complete +0.5 ms, call +0.1 ms.
- The raw baseline creates an `HttpClient` per test, matching ProtoTest's per-test model. A raw suite that
  reuses one client is faster.

One lifecycle broken into the phases the framework owns, on a bare host with no application, 64 warmups and 512
measured iterations per phase, medians. Source locations are on (the default), with `EmbedSources = false` and
`EmbedArtifacts = false`:

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

DI scope creation, context creation, clock registration, attribute and skip resolution and teardown all stay
under 0.04 ms. The OpenCSMS tracing-off leg is the most order-sensitive, because it runs second, after the first
leg has created and deleted about 3,300 tap queues on the shared broker. The controlled tap A/B,
`PerTestTapLifecycle_ShouldStayWithinTheSanityBound` in the RabbitMQ suite, measures 0.03 ms with no tap
destinations and about 11-12 ms for three.

### Re-running

```
dotnet test tests/ProtoTest.Core.Tests --filter TraceScaleTests
```

The harness prints `[scale] tests=... trace=... run=... stop+export=... allocated=...` for each size. Its assertions are sanity bounds, so a trace must stay proportional to the test count. They are not performance targets.

The overhead comparison re-runs with:

```
dotnet test tests/ProtoTest.AspNetCore.Tests --filter Category=Benchmark --logger "console;verbosity=detailed"
```

It prints `[overhead]` lines per mode: the short test written both ways, the single-request comparison, the bare lifecycle and each side's startup. Its assertions are generous sanity bounds too, not targets.

The per-test phase profile re-runs with:

```
dotnet test tests/ProtoTest.Core.Tests --filter PerTestPhaseBenchmarkTests --logger "console;verbosity=detailed"
```

The broker tap comparison re-runs with (a broker from `ProtoTest__Messaging__RabbitMq__ConnectionString` or a container runtime):

```
dotnet test tests/ProtoTest.Messaging.RabbitMq.Tests --filter PerTestTapLifecycle --logger "console;verbosity=detailed"
```

</details>

## Related

- [Concurrency](../foundation/concurrency.md#exercised-parallelism): the parallel runs, from 8 to 64 workers.
- [The viewer's own measurements](https://github.com/MSeys/ProtoTest/blob/main/viewer/README.md#performance-at-1000-tests): how the viewer was measured on a 1,200-test trace.
