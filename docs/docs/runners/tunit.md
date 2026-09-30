---
sidebar_position: 6
title: TUnit
description: "Register ProtoTest with TUnit: the test executor, the assembly hooks, TUnit's own [Test] attribute, what the adapter changes and its limits."
---

# TUnit

TUnit is wired differently from the other four. There is no ProtoTest test attribute. You keep TUnit's own `[Test]`. ProtoTest runs through the TUnit executor.

## Install

```bash
dotnet add package ProtoTest.TUnit
```

ProtoTest targets **.NET 8, 9 and 10**, and needs **TUnit 1.66.0 or newer**. The `dotnet new prototest` template defaults to `net10.0`; pass `--framework net8.0` or `--framework net9.0` for an older runtime.

## Register

Two things: register the executor for the assembly, and initialize the host from TUnit's assembly hooks.

```csharp
using ProtoTest.Core;
using ProtoTest.TUnit;
using TUnit.Core;

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

The `TUnit` namespace carries TUnit's own `[Test]`, `[Before]` and `[After]`.

`[assembly: TestExecutor<ProtoTestExecutor>()]` applies the executor to every test in the assembly. Register it there. The class and method forms of `[TestExecutor<T>]` are not part of the supported surface.

**On .NET SDK 10, a TUnit project runs on Microsoft.Testing.Platform.** The `dotnet new prototest` template carries the opt-in:

```json title="global.json"
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

With that file in the project or solution directory, run `dotnet test` from that directory, or `dotnet test --project Starter.Tests/Starter.Tests.csproj` from anywhere. Running from elsewhere without it fails before any test runs: the old VSTest path reports that testing with the VSTest target is no longer supported.

```bash
dotnet test                                               # right: run from the global.json directory
dotnet test --project Starter.Tests/Starter.Tests.csproj  # right: name the project, from anywhere
```

A test is a normal TUnit test. The executor wraps it. The interception path:

```text
[Test] → ProtoTestExecutor.ExecuteTest → resolve MethodInfo + attributes
  → skip? → Skip.Test(reason), nothing starts, no trace
  → else start the context → await the body → complete with the outcome
  → body threw? → record, complete, rethrow via ExceptionDispatchInfo
```

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;
using TUnit.Core;

namespace Orders.Tests;

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

## The context window

`|` is the host bar, `[]` is the context. The executor wraps the body:

```text
|[Before(Assembly) / After(Assembly) (host)]|  [[executor wraps body]]
```

## What the adapter changes

| Item | What the adapter does |
| --- | --- |
| The host | `ProtoTestAssembly` gives you `InitializeAsync` and `CleanupAsync` for TUnit's `[Before(Assembly)]` and `[After(Assembly)]`. |
| The test attribute | None. You keep TUnit's `[Test]`, and `ProtoTestExecutor` intercepts the execution. |
| The lifecycle | `ExecuteTest` resolves the test's `MethodInfo` and attributes, starts the context, awaits the test action, then completes the context. |
| Failure handling | If the body throws, the executor records the outcome, completes the context, and rethrows with `ExceptionDispatchInfo`, so TUnit still sees the exception. A teardown failure never replaces the body's outcome. |
| Scheduling | Fully async, like xUnit v2 and MSTest. |
| Cancellation | The live `TestContext.CancellationToken` is passed into the lifecycle, so the test, its hooks and the SQL connect wait observe TUnit's per-test token. |
| Outcomes | A completed body is `Passed`. A thrown `SkipTestException` (a body-level `Skip.Test`) is `Skipped`. A thrown `OperationCanceledException` is `Cancelled`. Anything else is `Failed` with the exception, rethrown to TUnit. |
| Skips | Skip conditions are evaluated before the lifecycle starts and raised through `TUnit.Core.Skip.Test(reason)`, so nothing starts and no trace entry is written. |
| Attachments | The live `TestContext` is passed to the attachment publisher, which calls `context.Output.AttachArtifact(path, name, description)`. Per test and parallel-safe. |
| Test names | `DeclaringType.MethodName[args]` through `ProtoTestName.ForRow` when the test has arguments, so parallel rows stay apart. |

## Limits

- There is no ProtoTest attribute. Test discovery and `[Test]` are entirely TUnit's.
- Register the executor for the assembly. `[TestExecutor<T>]` on a class or a method is not part of the supported surface.
- **Runs unwrapped.** A source-generated test without a reflection `MethodInfo` runs unwrapped. The executor has no method data to build a context.
- The per-test token comes from `TestContext.CancellationToken`: the executor has no token of its own, so a test with no live context (see the `MethodInfo` limit) starts from `CancellationToken.None`.
- A teardown failure is recorded but can never change the body's outcome.

## Learn more

- [Test runners](./overview.md): the five adapters side by side.
- [Skip conditions](../foundation/skip-conditions.md): the conditions every adapter evaluates.
- [Lifecycle](../foundation/lifecycle.md): the hooks and attributes around a test.
