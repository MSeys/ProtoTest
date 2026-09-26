---
sidebar_position: 4
title: Benchmarks
description: "Measured trace size, run time, export time and memory growth for synthetic suites, and the levers that change them."
---

# Benchmarks

These numbers come from the in-repo harnesses - `tests/ProtoTest.Core.Tests/TraceScaleTests.cs` for trace size and scale, `tests/ProtoTest.AspNetCore.Tests/OverheadBenchmarkTests.cs` for the `WebApplicationFactory` comparison - on an AMD Ryzen 7 9800X3D (8 cores, 16 threads), Windows 11, .NET 8 (the test projects' target). They are indicative, not a contract: run the harnesses on your own hardware and CI image before quoting them, and expect run-to-run medians to move by around 20%.

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

## Levers

| Lever | Effect |
| --- | --- |
| `trace.CaptureSourceLocations = false` | Drops the `code.file.path` / `code.line.number` / `code.function.name` attributes |
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

## Parallel execution

The demo suite - containers and real browsers included - is stable at 8 and 32 workers on a 16-core machine and
showed one non-reproducible failure at 64 workers. The [concurrency page](foundation/concurrency.md#exercised-parallelism)
records the runs and the failure mode.
