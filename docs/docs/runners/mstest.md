---
sidebar_position: 5
title: MSTest
description: "Set up ProtoTest with MSTest: the assembly initialize and cleanup hooks, [ProtoTest] tests, outcomes and attachments."
---

# MSTest

```bash
dotnet add package ProtoTest.MSTest
```

## Assembly setup

MSTest's `ProtoTestAssembly` has no lifecycle attributes of its own — it gives you two protected static helpers that you call from `[AssemblyInitialize]` and `[AssemblyCleanup]`.

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

## Writing a test

`[ProtoTest]` derives from `TestMethodAttribute`, so it **replaces** `[TestMethod]`.

```csharp
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ProtoTest.Core;
using ProtoTest.MSTest;
using ProtoTest.Rest;
using System.Net;

[TestClass]
[Application("Api")]
public class OrderTests
{
    [ProtoTest]
    public async Task Orders_endpoint_responds()
    {
        var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.ShouldHaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## Things to know

**Fully async.** MSTest's `ExecuteAsync(ITestMethod)` is awaited properly — no sync-over-async, unlike xUnit v2.

**Outcomes are recorded**: all-passed maps to Passed, all-ignored to Skipped, and Failed / Error / Timeout / Aborted to Failed with the exception (or `MSTest.{outcome}` when there isn't one).

**Attachments** are materialised to disk and appended to the **first data-row result's** `TestResult.ResultFiles` — the lifecycle spans every data row, so attaching once avoids duplicating them — and they show up in the `.trx` output.

**Parallelism** works with the usual MSTest switch:

```csharp
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
```
