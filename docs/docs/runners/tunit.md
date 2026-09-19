---
sidebar_position: 6
title: TUnit
description: "Set up ProtoTest with TUnit: the test executor, the assembly hooks, [Test] tests and outcomes."
---

# TUnit

```bash
dotnet add package ProtoTest.TUnit
```

TUnit is wired differently from the other four: **there is no ProtoTest test attribute**. You keep TUnit's own `[Test]`, and ProtoTest hooks in through TUnit's executor mechanism.

## Assembly setup

Two things: register the executor for the assembly, and initialise the host from TUnit's assembly hooks.

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

## Writing a test

Just a normal TUnit test. The executor wraps it.

```csharp
using ProtoTest.Core;
using ProtoTest.Rest;
using System.Net;

[Application("Api")]
public class OrderTests
{
    [Test]
    public async Task Orders_endpoint_responds()
    {
        var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.ShouldHaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## Things to know

**`Cancelled` is a first-class outcome.** `ProtoTestExecutor` maps a successful run to Passed, an `OperationCanceledException` to Cancelled, and anything else to Failed — rethrowing in both failure cases so TUnit still sees the exception. xUnit v2 with `[ProtoTestFact]` / `[ProtoTestTheory]` also records cancellation.

**Attachments are per-test and parallel-safe.** The publisher is constructed with the live `TestContext` and calls `context.Output.AttachArtifact(path, name, description)`, so artifacts land on the right test even under heavy parallelism.
