---
sidebar_position: 4
title: Benchmarks
description: "Measured trace size, run time, export time and memory growth for synthetic suites, and the levers that change them."
---

# Benchmarks

These numbers come from the in-repo harness (`tests/ProtoTest.Core.Tests/TraceScaleTests.cs`) on Windows 11, .NET 8, a developer laptop. They are indicative, not a contract: run the harness on your own hardware and CI image before quoting them.

| Tests | Trace size | Run time | Stop + export | Allocated | Peak working set growth |
| --- | --- | --- | --- | --- | --- |
| 100 | 0.70 MB | 78 ms | 49 ms | 38 MB | 44 MB |
| 1,000 | 6.95 MB | 389 ms | 59 ms | 377 MB | 23 MB |

Every synthetic test records one operation, one event, one entity-state change and one observation - roughly the evidence a small API test produces. That is about 7 KB of trace and about 0.4 MB of allocation per test at this size. The harness runs with `EmbedSources = false` and `EmbedArtifacts = false`; the defaults (embedding the suite's source files and attachment bytes) produce a larger file.

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

## Parallel execution

The demo suite - containers and real browsers included - is stable at 8 and 32 workers on a 16-core machine and
showed one non-reproducible failure at 64 workers. The [concurrency page](foundation/concurrency.md#exercised-parallelism)
records the runs and the failure mode.
