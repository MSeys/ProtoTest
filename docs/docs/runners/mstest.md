---
sidebar_position: 5
title: MSTest
description: "Set up ProtoTest with MSTest: the assembly initialize and cleanup hooks, [ProtoTest] tests, outcomes and attachments."
---

# MSTest

`ProtoTest.MSTest` gives you two static helpers to call from MSTest's assembly hooks, plus a `[ProtoTest]` attribute that wraps each test and all of its data rows in one ProtoTest context.

## Install

```bash
dotnet add package ProtoTest.MSTest --prerelease
```

ProtoTest targets **.NET 8, 9 and 10**. The `dotnet new prototest` template defaults to `net10.0`; pass `-f net8.0` or `-f net9.0` for an older runtime.

## Enable it

`ProtoTestAssembly` has no lifecycle attributes of its own — it gives you two protected static helpers that you call from `[AssemblyInitialize]` and `[AssemblyCleanup]`.

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;
using ProtoTest.MSTest;
using ProtoTest.Rest;

[TestClass]
public class Setup : ProtoTestAssembly
{
    [AssemblyInitialize]
    public static Task AssemblyInitializeAsync(TestContext context) =>
        InitializeAsync(builder =>
            builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api"))));

    [AssemblyCleanup]
    public static Task AssemblyCleanupAsync() => CleanupAsync();
}
```

`InitializeAsync` throws if the host has already been created, so call it exactly once.

## Quick start

`[ProtoTest]` derives from `TestMethodAttribute`, so it **replaces** `[TestMethod]`; it overrides `ExecuteAsync` to run the ProtoTest lifecycle around MSTest's invocation.

```csharp
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;
using ProtoTest.MSTest;
using ProtoTest.Rest;

[TestClass]
[Application("Api")]
public class OrderTests
{
    [ProtoTest]
    public async Task Orders_endpoint_responds()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## Per-test lifecycle

`ExecuteAsync` is awaited properly — no sync-over-async. It starts the context once, runs `base.ExecuteAsync` (every data row), then completes the context in a `finally`, so the trace shows one test whose body ran all rows.

## Outcomes

| MSTest result | ProtoTest records | Why |
| --- | --- | --- |
| all rows `Passed` | `Passed` | |
| all rows `Ignored` or `Inconclusive` | `Skipped` | nothing ran |
| passed rows mixed with ignored/inconclusive rows | `Partial` | Passed would hide the skip; Skipped would hide the rows that ran |
| first row `Failed`, `Error`, `Timeout` or `Aborted` | `Failed` | with `TestFailureException` when there is one, else error type `MSTest.{Outcome}` |
| no results, or an unmapped outcome | `Unknown` | |

## Skipping

MSTest 4.4 has no public dynamic-skip API, so a condition returns an ignored `TestResult` before the lifecycle starts. Its `IgnoreReason` is internal; the reason travels on the public `LogOutput` and is prefixed to the display name — `Orders_endpoint_responds (skipped: {reason})`. The test reads as skipped and still says why. See [Skip conditions](../foundation/skip-conditions.md).

## Attachments

Artifacts are materialized to disk and appended to the **first** data-row result's `TestResult.ResultFiles` — the lifecycle spans every data row, so attaching once avoids duplicating them — and they show up in the `.trx` output.

## Parallelism

Parallelism works with the usual MSTest switch:

```csharp
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
```

## Limits

- The skip reason is not a first-class MSTest property; it only reaches the display name and `LogOutput`.
- Attachments land on the first data row only — later rows don't carry copies.
- One data-row set is one ProtoTest test, not one per row.

## Next

- [Test runners](./overview.md) — the same setup for the other four runners.
- [Skip conditions](../foundation/skip-conditions.md) — the conditions every adapter evaluates.
