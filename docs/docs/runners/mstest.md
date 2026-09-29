---
sidebar_position: 5
title: MSTest
description: "Register ProtoTest with MSTest: the assembly hooks, the [ProtoTest] attribute, what the adapter changes and its limits."
---

# MSTest

`ProtoTest.MSTest` gives you two static helpers to call from MSTest's assembly hooks, plus a `[ProtoTest]` attribute that wraps each invocation in one ProtoTest context. MSTest invokes the attribute once per data row, so each row is its own context and its own trace.

## Install

```bash
dotnet add package ProtoTest.MSTest
```

ProtoTest targets **.NET 8, 9 and 10**, and needs **MSTest.TestFramework 4.0.2 or newer**; the standard `dotnet new mstest` template already pins it. The `dotnet new prototest` template defaults to `net10.0`; pass `-f net8.0` or `-f net9.0` for an older runtime.

## Register

`ProtoTestAssembly` has no lifecycle attributes of its own. It gives you two protected static helpers to call from `[AssemblyInitialize]` and `[AssemblyCleanup]`:

```csharp
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

The test attribute is `[ProtoTest]`. It derives from `TestMethodAttribute`, so it replaces `[TestMethod]`:

```csharp
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

## What the adapter changes

| Item | What the adapter does |
| --- | --- |
| The host | Two static helpers, `InitializeAsync` and `CleanupAsync`, start and stop the one host for the assembly. |
| The test attribute | `[ProtoTest]` replaces `[TestMethod]` and overrides `ExecuteAsync` to run the lifecycle around MSTest's invocation. |
| The lifecycle | One context per data row. `ExecuteAsync` is called once per row, starts a context, runs the body, and completes the context in a `finally`: one row, one trace. |
| Attachments | Each row's published files are appended to that row's `TestResult.ResultFiles`, and they show up in the `.trx` output. |
| Scheduling | `ExecuteAsync` is awaited properly. There is no sync-over-async. |
| Cancellation | `TestMethodAttribute.ExecuteAsync(ITestMethod)` exposes no token, so the test starts with `CancellationToken.None`. |
| Outcomes | `Passed` to `Passed`. `Ignored`, `Inconclusive` and `NotRunnable` to `Skipped`. A failure with an exception goes through the shared classifier, so a cancelled exception maps to `Cancelled`. A failure without one records `MSTest.{Outcome}`. `Timeout` and `Aborted` map to `Cancelled`. Anything else is `Unknown`. |
| Skips | A skip condition returns an ignored `TestResult` before the lifecycle starts. MSTest 4.4 has no public dynamic-skip API, so the reason travels on the public `LogOutput` and is prefixed to the display name: `Orders_endpoint_responds (skipped: {reason})`. |
| Test names | `DeclaringType.MethodName[args]` through `ProtoTestName.ForRow`, so parallel rows stay apart. |
| Parallelism | The usual switch works: `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]`. |

## Limits

- MSTest 4.4 has no public dynamic-skip API. The reason is not a first-class MSTest property; it only reaches the display name and `LogOutput`.
- `TestMethodAttribute.ExecuteAsync(ITestMethod)` exposes no cancellation token, so a test starts with `CancellationToken.None`.
- There is no assembly-wide auto-wrap. MSTest offers no assembly hook for one, so every test method carries `[ProtoTest]`.
- A skipped test is reported only by MSTest. ProtoTest records nothing for it.

## Learn more

- [Test runners](./overview.md): the five adapters side by side.
- [Skip conditions](../foundation/skip-conditions.md): the conditions every adapter evaluates.
- [Lifecycle](../foundation/lifecycle.md): the hooks and attributes around a test.
