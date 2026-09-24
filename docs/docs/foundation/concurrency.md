---
sidebar_position: 7
title: Concurrency
description: "How ProtoTest keeps parallel tests isolated, what to do when a test fans out internally, and the parallelism the project has actually exercised."
---

# Concurrency

ProtoTest scopes the execution context to the async flow: `Proto.Context` is per test, the ambient value is
flow-local, and two tests running in parallel cannot see each other's clients, resources or trace. That is why
the runner's own parallelism is safe to use, and why the repository's suites run with
`[Parallelizable(ParallelScope.All)]`.

## Inside a test

The context flows into anything a test awaits. Two patterns matter:

- **Fan-out that keeps the flow works.** A `Task.WhenAll` over tasks you start in the test body keeps the
  ambient context, so the operations those tasks record land on the test's trace.
- **Suppressed or abandoned flow loses the context.** `ExecutionContext.SuppressFlow()`, a detached `Thread`, or
  work started after the context was cleared run without `Proto.Context` - reading it throws, and anything they
  record lands on the run trace instead of the test's. Keep fan-out on the test's flow, or pass the
  `ProtoExecutionContext` explicitly to the delegate that needs it.

One test owns one context while it is active; a nested lifecycle cannot start on the same flow.

## Exercised parallelism

Measured on a 16-core developer machine with the demo suite - Postgres and RabbitMQ containers owned by the run,
plus real browsers - using `NUnit.NumberOfTestWorkers`:

| Workers | Result | Duration |
| --- | --- | --- |
| 8 | 53 passed, 6 skipped | 6 s |
| 32 | 53 passed, 6 skipped | 8 s |
| 64 | 1 failed, then passed on rerun | 21 s |

The suite is stable up to roughly twice the core count. At four times the cores (64 workers on 16 cores) the
run oversubscribed and produced one non-reproducible failure; the demo writes a single trace file per run, so
that failure's evidence was overwritten before it could be read. Beyond this range, expect resource pressure
rather than a ProtoTest-specific limit: port and container exhaustion, browser memory, and contention on
run-scoped infrastructure. The project's own suites run at `LevelOfParallelism(8)`; the demo at 8 workers is the
configuration exercised routinely.

_These numbers are indicative, not a contract. Re-run the demo on your own hardware before quoting them._
