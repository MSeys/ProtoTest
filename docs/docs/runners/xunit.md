---
sidebar_position: 2
title: xUnit v2
description: "Register ProtoTest with xUnit v2: the collection fixture, [ProtoTestFact] and [ProtoTestTheory], what the adapter changes and its limits."
---

# xUnit v2

`ProtoTest.Xunit` wraps xUnit v2's runner. A collection fixture starts the host once per test process, and `[ProtoTestFact]` / `[ProtoTestTheory]` start and complete a ProtoTest context around each test.

## Install

```bash
dotnet add package ProtoTest.Xunit
```

ProtoTest targets **.NET 8, 9 and 10**, and needs **xunit 2.9.3 or newer**; the standard `dotnet new xunit` template already pins it. The `dotnet new prototest` template defaults to `net10.0`; pass `-f net8.0` or `-f net9.0` for an older runtime.

## Register

`ProtoTestAssembly` implements xUnit's `IAsyncLifetime`, so it works as a collection fixture. Every test class that needs ProtoTest joins that collection.

```csharp
using ProtoTest.Core;
using ProtoTest.Xunit;
using Xunit;

public class ProtoTestFixture : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api")));
}

[CollectionDefinition(Name)]
public class ProtoTestCollection : ICollectionFixture<ProtoTestFixture>
{
    public const string Name = "ProtoTest Collection";
}
```

The test attributes are `[ProtoTestFact]` and `[ProtoTestTheory]`. They derive from `FactAttribute` and `TheoryAttribute`, so they replace them outright. Do not add `[Fact]` as well.

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit;

namespace Orders.Tests;

[Collection(ProtoTestCollection.Name)]
[Application("Api")]
public class OrderTests
{
    [ProtoTestFact]
    public async Task Orders_endpoint_responds()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

A `[ProtoTestTheory]` behaves the same way, and each `[InlineData]` row is a test of its own.

:::warning[Forgetting `[Collection]`]
Without `[Collection(ProtoTestCollection.Name)]` the fixture never runs, so `ProtoTestAssembly.Host` throws `InvalidOperationException` from inside the test. xUnit v2 surfaces that as a test-host crash: the run aborts and the other tests' results are lost, instead of one test failing. Treat the attribute as mandatory on every class that carries a ProtoTest attribute or reads `Proto.Context`.
:::

## Bring an existing suite

Adoption is per test, not per project. A plain `[Fact]` or `[Theory]` keeps running unchanged, and a class that joins the collection can mix converted and plain tests: only `[ProtoTestFact]` and `[ProtoTestTheory]` start a context. Convert a class when its tests need a host, a trace or capability skips. Add the collection attribute in the same change: the fixture starts the host. [Bring an existing xUnit suite](./bring-your-existing-suite.md) walks the order.

## What the adapter changes

| Item | What the adapter does |
| --- | --- |
| The host | Starts once through the collection fixture. A class that does not join the collection has no host. |
| The test attributes | `[ProtoTestFact]` and `[ProtoTestTheory]` replace `[Fact]` and `[Theory]`, with `ProtoTest.Xunit.Sdk.ProtoTestFactDiscoverer` and `ProtoTest.Xunit.Sdk.ProtoTestTheoryDiscoverer` behind them. |
| The lifecycle | `ProtoXunitTestRunner.InvokeTestMethodAsync` starts the context, runs the test, then completes it in a `finally` with the outcome xUnit recorded, so the context completes even when xUnit's own pipeline throws. |
| The context window | The context starts before class construction: the test class constructor and the before- and after-attributes already see `Proto.Context`. xUnit v3's constructor does not. |
| Row handling | Each row is its own `ProtoXunitTestRunner` under xUnit's display name, with the arguments included, so the rows stay apart in the trace. |
| Scheduling | Fully async. Nothing blocks on a task. |
| Cancellation | The runner's `CancellationTokenSource` token rides the lifecycle. A body `OperationCanceledException` and a signalled source both record `Cancelled`. |
| Outcomes | `passed` to `Passed`, `failed` to `Failed` with the exception, a failed `OperationCanceledException` to `Cancelled`, and a cancelled case to `Cancelled`. A skipped test records nothing, because xUnit never invokes it. |
| Skips | Skip conditions are evaluated in the runner's constructor, before xUnit invokes anything, and the reason becomes xUnit's `SkipReason`. |
| Attachments | xUnit v2 has no attachment API. ProtoTest writes in-memory content under `%TEMP%\ProtoTest\attachments` and prints the path to the console: `ProtoTest attachment 'rest-01-response': /path/to/...`. The message needs `--logger "console;verbosity=detailed"` to show. |
| Test names | xUnit's display name, with the row's arguments included. |

## Limits

- No native attachments. In-memory content is written under `%TEMP%\ProtoTest\attachments`, not into xUnit's output, and the path travels on the console.
- No dynamic skip. The reason is decided before the test method is invoked, so it cannot depend on the body.
- Every test class must join the collection. The host is never initialized otherwise, and the resulting crash aborts the whole run.
- The context starts before class construction, so a test class constructor already sees `Proto.Context`. The same constructor runs before the context in xUnit v3.
- A skipped test exists only in xUnit's output. ProtoTest records nothing for it.
- Theory rows are recorded under xUnit's display name, unlike MSTest and TUnit, which compose `MethodName[args]`.

## Learn more

- [Test runners](./overview.md): the five adapters side by side.
- [Skip conditions](../foundation/skip-conditions.md): the conditions every adapter evaluates.
- [Lifecycle](../foundation/lifecycle.md): the hooks and attributes around a test.
