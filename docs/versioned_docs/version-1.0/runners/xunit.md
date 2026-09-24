---
sidebar_position: 2
title: xUnit v2
description: "Set up ProtoTest with xUnit v2: the collection fixture, [ProtoTestFact] tests, theories and outcomes."
---

# xUnit v2

`ProtoTest.Xunit` wraps xUnit v2's runner: a collection fixture starts the host once per test process, and `[ProtoTestFact]` / `[ProtoTestTheory]` start and complete a ProtoTest context around each test.

## Install

```bash
dotnet add package ProtoTest.Xunit
```

ProtoTest targets **.NET 8, 9 and 10**. The `dotnet new prototest` template defaults to `net10.0`; pass `-f net8.0` or `-f net9.0` for an older runtime.

## Enable it

`ProtoTestAssembly` implements xUnit's `IAsyncLifetime`, so it works as a collection fixture. Every test class that needs ProtoTest joins that collection.

```csharp
using ProtoTest.Core;
using ProtoTest.Rest;
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

:::warning[Forgetting `[Collection]`]
Without `[Collection(ProtoTestCollection.Name)]` the fixture never runs. `ProtoTestAssembly.Host` throws `InvalidOperationException` and the test fails before its body.
:::

## Quick start

`[ProtoTestFact]` and `[ProtoTestTheory]` derive from `FactAttribute` and `TheoryAttribute`, so they replace them outright — don't add `[Fact]` as well. The discoverers are `ProtoTest.Xunit.Sdk.ProtoTestFactDiscoverer` and `ProtoTest.Xunit.Sdk.ProtoTestTheoryDiscoverer`.

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit;
using Xunit;

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

A `[ProtoTestTheory]` behaves the same way; each `[InlineData]` row is a test of its own.

## Per-test lifecycle

`ProtoXunitTestRunner.InvokeTestMethodAsync` starts the context with xUnit's display name, runs the test, then completes it with the outcome xUnit recorded. If setup fails the context is already rolled back, the failure goes into xUnit's aggregator, and the test fails.

Each theory row is its own test: the runner creates one `ProtoXunitTestRunner` per row, recorded under xUnit's display name with the arguments included — `….Invoices_filter_by_state(state: "open")` — so the trace keeps them apart.

## Outcomes

| xUnit reports | ProtoTest records | Why |
| --- | --- | --- |
| passed | `Passed` | nothing failed in xUnit's aggregator |
| failed | `Failed` with the exception | the aggregator's exception is recorded and rethrown by xUnit |
| cancelled | `Cancelled` | the runner's `CancellationTokenSource` was signalled |
| skipped | nothing | xUnit never invokes a skipped test, so no context starts |

The lifecycle is fully async — nothing blocks on a task.

## Skipping

Skip conditions are evaluated in the runner's constructor, before xUnit invokes anything: `ProtoTestSkip.GetReason` resolves the test's attributes and the result becomes xUnit's `SkipReason`. `[RequiresCapability]`, `[RequiresInProcess]` and `[RequiresPlaywrightBrowser]` therefore read as normal xUnit skips with their reason, and the test produces no trace entry. See [Skip conditions](../foundation/skip-conditions.md).

## Attachments

xUnit v2 has no attachment API, so each artifact is materialized to a file and its path is written to the console:

```
ProtoTest attachment 'rest-01-response': /path/to/TestResults/.../rest-01-response.json
```

## Limits

- No native attachments — artifacts land next to the trace, not in xUnit's output.
- No dynamic skip: the reason is decided before the test method is invoked, so a condition cannot depend on the body.
- Every test class must join the collection; the host is never initialized otherwise.
- Theory rows are recorded under xUnit's display name, unlike the fully qualified names the other adapters use.

## Next

- [Test runners](./overview.md) — the same setup for the other four runners.
- [Skip conditions](../foundation/skip-conditions.md) — the conditions every adapter evaluates.
