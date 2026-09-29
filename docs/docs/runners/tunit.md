---
sidebar_position: 6
title: TUnit
description: "Register ProtoTest with TUnit: the test executor, the assembly hooks, TUnit's own [Test] attribute, what the adapter changes and its limits."
---

# TUnit

TUnit is wired differently from the other four. There is no ProtoTest test attribute: you keep TUnit's own `[Test]`, and ProtoTest hooks in through TUnit's executor mechanism.

## Install

```bash
dotnet add package ProtoTest.TUnit
```

ProtoTest targets **.NET 8, 9 and 10**, and needs **TUnit 1.66.0 or newer**. The `dotnet new prototest` template defaults to `net10.0`; pass `-f net8.0` or `-f net9.0` for an older runtime.

## Register

Two things: register the executor for the assembly, and initialize the host from TUnit's assembly hooks.

```csharp
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

`[assembly: TestExecutor<ProtoTestExecutor>()]` applies the executor to every test in the assembly. TUnit also allows `[TestExecutor<T>]` at class or method scope, but only the assembly form is exercised in this repository, so treat narrower scoping as untested.

A test is a normal TUnit test. The executor wraps it.

```csharp
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

## What the adapter changes

| Item | What the adapter does |
| --- | --- |
| The host | `ProtoTestAssembly` gives you `InitializeAsync` and `CleanupAsync` for TUnit's `[Before(Assembly)]` and `[After(Assembly)]`. |
| The test attribute | None. You keep TUnit's `[Test]`, and `ProtoTestExecutor` intercepts the execution. |
| The lifecycle | `ExecuteTest` resolves the test's `MethodInfo` and attributes, starts the context, awaits the test action, then completes the context. |
| Failure handling | If the body throws, the executor records the outcome, completes the context, and rethrows with `ExceptionDispatchInfo`, so TUnit still sees the exception. A teardown failure never replaces the body's outcome. |
| Scheduling | Fully async, like xUnit v2 and MSTest. |
| Cancellation | `ITestExecutor` exposes no token, so the test starts with `CancellationToken.None`. |
| Outcomes | A completed body is `Passed`. A thrown `SkipTestException` (a body-level `Skip.Test`) is `Skipped`. A thrown `OperationCanceledException` is `Cancelled`. Anything else is `Failed` with the exception, rethrown to TUnit. |
| Skips | Skip conditions are evaluated before the lifecycle starts and raised through `TUnit.Core.Skip.Test(reason)`, so nothing starts and no trace entry is written. |
| Attachments | The live `TestContext` is passed to the attachment publisher, which calls `context.Output.AttachArtifact(path, name, description)`. Per test and parallel-safe. |
| Test names | `DeclaringType.MethodName[args]` through `ProtoTestName.ForRow` when the test has arguments, so parallel rows stay apart. |

## Limits

- There is no ProtoTest attribute. Test discovery and `[Test]` are entirely TUnit's.
- Only the assembly-level executor is exercised in this repository. `[TestExecutor<T>]` on a class or method is untested.
- A source-generated test that exposes no reflection `MethodInfo` runs unwrapped, because the executor has nothing to prepare a context from.
- `ITestExecutor` exposes no cancellation token, so a test starts with `CancellationToken.None`.
- A teardown failure is recorded but can never change the body's outcome.

## Learn more

- [Test runners](./overview.md): the five adapters side by side.
- [Skip conditions](../foundation/skip-conditions.md): the conditions every adapter evaluates.
- [Lifecycle](../foundation/lifecycle.md): the hooks and attributes around a test.
