---
sidebar_position: 7
title: Concurrency
description: "How ProtoTest keeps parallel tests isolated, what to do when a test fans out internally, and the parallelism the project has actually exercised."
---

# Concurrency

## What it is

ProtoTest scopes the execution context to the async flow: `Proto.Context` is per test, the ambient value is flow-local, and two tests running in parallel cannot see each other's clients, resources or trace. That is why the runner's own parallelism is safe to use, and why the repository's suites run with `[Parallelizable(ParallelScope.All)]`.

## Inside a test

The context flows into anything a test awaits. Two patterns matter:

- **Fan-out that keeps the flow works.** A `Task.WhenAll` over tasks you start in the test body keeps the ambient context, so the operations those tasks record land on the test's trace.
- **Suppressed or abandoned flow loses the context.** `ExecutionContext.SuppressFlow()`, a detached `Thread`, or work started after the context was cleared run without `Proto.Context`. Reading it throws, and anything they record lands on the run trace instead of the test's. Keep fan-out on the test's flow, or pass the `ProtoExecutionContext` explicitly to the delegate that needs it.

One test owns one context while it is active. A nested lifecycle cannot start on the same flow.

## Exercised parallelism

Measured on a 16-core developer machine with the demo suite, which owns Postgres and RabbitMQ containers, plus real browsers, using `NUnit.NumberOfTestWorkers`:

| Workers | Result | Duration |
| --- | --- | --- |
| 8 | 53 passed, 6 skipped | 6 s |
| 32 | 53 passed, 6 skipped | 8 s |
| 64 | 1 failed, then passed on rerun | 21 s |

The suite is stable up to roughly twice the core count. At four times the cores (64 workers on 16 cores) the run oversubscribed and produced one non-reproducible failure. The demo writes a single trace file per run, so that failure's evidence was overwritten before it could be read.

Beyond this range, expect resource pressure rather than a ProtoTest-specific limit: port and container exhaustion, browser memory, and contention on run-scoped infrastructure. The project's own suites run at `LevelOfParallelism(8)`. The demo at 8 workers is the configuration exercised routinely.

These numbers are indicative, not a contract. Re-run the demo on your own hardware before quoting them.

## What the trace shows

- Operations recorded on the test's flow land on that test's record, whichever task recorded them.
- Operations from a suppressed or abandoned flow land on the run trace, without a test to attribute them to. They are still correlated by trace id.
- Each test's record carries its own id, so two parallel tests writing similar-looking requests stay distinguishable.

## Limits

- **The context is flow-local.** Anything that escapes the flow cannot read `Proto.Context`; pass the context explicitly or correlate by trace id with `ProtoHost.FindTraceWriter`.
- **One context per active test per flow.** Starting a second test on the same flow throws.
- **Parallelism belongs to the runner.** ProtoTest does not schedule tests; configure workers with your runner's own options.
- **Shared run-scoped pieces are shared.** Containers, a per-run fake or a shared client serve every parallel test, so plan for concurrent use. See [Clients](./clients.md) and [Infrastructure](./infrastructure.md).
- **The measured ceiling is resource pressure.** Oversubscribing workers costs ports, containers, browser memory and database connections, not ProtoTest correctness.
