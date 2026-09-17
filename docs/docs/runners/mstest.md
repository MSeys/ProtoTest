---
sidebar_position: 5
title: MSTest
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
            builder.AddRest(rest => rest.AddClient("Api", "https://api.example.test/")));

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
public class OrderTests
{
    [ProtoTest]
    public async Task Orders_endpoint_responds()
    {
        var response = await Proto.Context.Rest("Api").GetAsync("/api/orders");
        response.ShouldHaveStatus(HttpStatusCode.OK);
    }
}
```

## Things to know

**Fully async.** MSTest's `ExecuteAsync(ITestMethod)` is awaited properly — no sync-over-async, unlike xUnit v2.

**Outcomes are recorded**: all-passed maps to Passed, all-ignored to Skipped, and Failed / Error / Timeout / Aborted to Failed with the exception (or `MSTest.{outcome}` when there isn't one).

**Attachments** are materialised to disk and appended to every `TestResult.ResultFiles`, so they show up in the `.trx` output.

**Parallelism** works with the usual MSTest switch:

```csharp
[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
```
