---
sidebar_position: 6
title: TUnit
description: "Set up ProtoTest with TUnit: the test executor, the assembly hooks, [Test] tests and outcomes."
---

# TUnit

TUnit is wired differently from the other four: **there is no ProtoTest test attribute**. You keep TUnit's own `[Test]`, and ProtoTest hooks in through TUnit's executor mechanism.

## Install

```bash
dotnet add package ProtoTest.TUnit
```

ProtoTest targets **.NET 8, 9 and 10**. The `dotnet new prototest` template defaults to `net10.0`; pass `-f net8.0` or `-f net9.0` for an older runtime.

## Enable it

Two things: register the executor for the assembly, and initialize the host from TUnit's assembly hooks.

```csharp
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.TUnit;
using TUnit.Core.Executors;

[assembly: TestExecutor<ProtoTestExecutor>()]

public class Setup : ProtoTestAssembly
{
    [Before(Assembly)]
    public static Task AssemblyInitializeAsync(AssemblyHookContext context) =>
        InitializeAsync(builder =>
            builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api"))));

    [After(Assembly)]
    public static Task AssemblyCleanupAsync(AssemblyHookContext context) => CleanupAsync();
}
```

`[assembly: TestExecutor<ProtoTestExecutor>()]` applies the executor to every test in the assembly. TUnit also allows `[TestExecutor<T>]` at class or method scope, but only the assembly form is exercised in this repository — treat narrower scoping as untested.

## Quick start

Just a normal TUnit test. The executor wraps it.

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;

[Application("Api")]
public class OrderTests
{
    [Test]
    public async Task Orders_endpoint_responds()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## Per-test lifecycle

`ProtoTestExecutor.ExecuteTest` resolves the test's `MethodInfo` and attributes, starts the context with the fully qualified name from `ProtoTestName.FromMethod`, awaits the test action, then completes the context; the live `TestContext` is what the attachment publisher writes to. It runs asynchronously, like xUnit v2 and MSTest.

If the body throws, the executor records the outcome, completes the context, and rethrows with `ExceptionDispatchInfo` so TUnit still sees the exception. Teardown never replaces the original failure.

## Outcomes

| What happens | ProtoTest records | Why |
| --- | --- | --- |
| the body completes | `Passed` | |
| the body throws `SkipTestException` (a body-level `Skip.Test`) | `Skipped` | a test that skips itself is not a failure |
| the body throws `OperationCanceledException` | `Cancelled` | with the exception |
| the body throws anything else | `Failed` | with the exception, rethrown to TUnit |

## Skipping

Skip conditions are evaluated before the lifecycle starts with `ProtoTestSkip.GetReason` and raised through `TUnit.Core.Skip.Test(reason)`, so nothing is started and no trace entry is written. A body-level skip is caught separately and still maps to `Skipped`. See [Skip conditions](../foundation/skip-conditions.md).

## Attachments

The publisher is constructed with the live `TestContext` and calls `context.Output.AttachArtifact(path, name, description)` — per-test and parallel-safe, so artifacts land on the right test even under heavy parallelism.

## Limits

- No ProtoTest attribute: test discovery and the `[Test]` attribute are entirely TUnit's.
- Narrower executor scoping (`[TestExecutor<T>]` on a class or method) is not exercised by this repository's tests.
- A teardown failure is recorded but can never change the body's outcome.

## Next

- [Test runners](./overview.md) — the same setup for the other four runners.
- [Skip conditions](../foundation/skip-conditions.md) — the conditions every adapter evaluates.
