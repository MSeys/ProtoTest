---
sidebar_position: 18
title: Concurrency
description: "How ProtoTest keeps parallel tests isolated, what to do when a test fans out internally, and the parallelism the project has actually exercised."
---

# Concurrency

## What it is

ProtoTest scopes the execution context to the async flow. `Proto.Context` is per test and flow-local. Two parallel tests do not share clients, resources or trace. That is why the runner's own parallelism is safe to use.

## Inside a test

The context flows into anything a test awaits. Two patterns matter:

- **Fan-out that keeps the flow works.** A `Task.WhenAll` over tasks you start in the test body keeps the ambient context, so the operations those tasks record land on the test's trace.
- **Suppressed or abandoned flow loses the context.** `ExecutionContext.SuppressFlow()`, a detached `Thread`, or work started after the context was cleared run without `Proto.Context`. Reading it throws, and anything they record lands on the run trace instead of the test's. Keep fan-out on the test's flow, or pass the `ProtoExecutionContext` explicitly to the delegate that needs it.

One test owns one context while it is active. A nested lifecycle cannot start on the same flow.

## Exercised parallelism

Recorded 2026-09-24 on a 16-core developer machine against a suite with Postgres, RabbitMQ and real browsers, using `NUnit.NumberOfTestWorkers`. Quote these numbers only after re-running your own suite on your own hardware:

| Workers | Result | Duration |
| --- | --- | --- |
| 8 | 53 passed, 6 skipped | 6 s |
| 32 | 53 passed, 6 skipped | 8 s |
| 64 | 1 failed, then passed on rerun | 21 s |

The suite was stable up to roughly twice the core count. At four times the cores the run oversubscribed and produced one failure that never reproduced. Its evidence was lost to a harness limitation: the suite writes a single trace file per run, so the passing rerun overwrote the failed run's trace before it could be read. There is no framework finding behind that row, only an unreadable artifact.

Re-run 2026-09-29 on a 16-core machine (`dotnet test -- NUnit.NumberOfTestWorkers=64`):

- `tests/ProtoTest.Core.Tests`: 380 passed, three runs, about 4 s each.
- `samples/Northstar.ProtoTest` with container Postgres, container RabbitMQ and a headless Chromium journey: 15 passed, 4 skipped, two runs (6 s and 2 s).

Beyond this range, expect resource pressure rather than a ProtoTest-specific limit: port and container exhaustion, browser memory, and contention on run-scoped infrastructure.

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
