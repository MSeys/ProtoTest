---
sidebar_position: 4
title: NUnit
description: "Set up ProtoTest with NUnit: the SetUpFixture, [ProtoTest] tests, parallel execution, outcomes and attachments."
---

# NUnit

`ProtoTest.NUnit` starts the host from a `[SetUpFixture]` and wraps each `[ProtoTest]` method in a ProtoTest context. The wrapper is applied outside NUnit's setup and teardown, so the lifecycle spans `[SetUp]`, the body and `[TearDown]`. It is the runner the repository's own sample suite uses.

## Install

```bash
dotnet add package ProtoTest.NUnit
```

ProtoTest targets **.NET 8, 9 and 10**. The `dotnet new prototest` template defaults to `net10.0`; pass `-f net8.0` or `-f net9.0` for an older runtime.

## Enable it

`ProtoTestAssembly` already carries `[SetUpFixture]` and owns `[OneTimeSetUp]` / `[OneTimeTearDown]`. Declare your own subclass — repeating `[SetUpFixture]` is harmless and makes the intent obvious.

```csharp
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder.AddApplication("Api", app => app
            .AddRest(rest => rest.AddClient("Api")));
}
```

:::warning[Namespace scoping]
This is NUnit behaviour, not ProtoTest's: a `[SetUpFixture]` **outside** any namespace applies to the whole assembly, while one **inside** a namespace applies only to that namespace and its children. If tests in another namespace can't find the host, that's usually why.
:::

## Quick start

`[ProtoTest]` derives from `TestAttribute`, so it **replaces** `[Test]`. It also implements NUnit's `IWrapSetUpTearDown`, which is how the lifecycle wraps `[SetUp]`, the body and `[TearDown]`.

```csharp
using System.Net;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

[TestFixture]
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

## Low-ceremony mode (opt-in)

A suite can skip `[ProtoTest]` on every method: apply the assembly-level auto-wrap once, and every plain `[Test]` runs through the same lifecycle.

```csharp
[assembly: ProtoTestAutoWrap]
```

NUnit applies the nearest `IWrapSetUpTearDown` attribute — method, then fixture, then assembly — so a test that already carries `[ProtoTest]` keeps its own wrapper and is never wrapped twice. The skip conditions, outcome mapping and attachments are the ones above, unchanged.

## Per-test lifecycle

`BeforeTest` resolves the method's attributes, evaluates its skip conditions, then starts the context with the fully qualified name from `ProtoTestName.FromMethod`; `AfterTest` maps NUnit's outcome and completes it. Both calls block on the host task (`GetAwaiter().GetResult()`), so NUnit is one of the two blocking adapters.

## Outcomes

| NUnit status | ProtoTest records | Why |
| --- | --- | --- |
| `Passed` | `Passed` | |
| `Failed` | `Failed` | error type is `NUnit.{Label}` — or `NUnit.Failed` when NUnit reports no label |
| `Skipped` | `Skipped` | |
| `Inconclusive` | `Skipped` | nothing was proven either way, so it reads like a skip |
| `Warning` | `Partial` | the test ran and passed with warnings attached |
| anything else | `Unknown` | the status has no ProtoTest equivalent |

## Skipping

The wrapper evaluates the test's conditions before anything runs - not even `[SetUp]` - by reporting an ignored result, so no trace entry is written for a skipped test. `[RequiresCapability]`, `[RequiresInProcess]` and `[RequiresPlaywrightBrowser]` all work this way. See [Skip conditions](../foundation/skip-conditions.md).

## Attachments

Artifacts go to `TestContext.AddTestAttachment(path, description)`, using the attachment's description or its name.

## Parallel execution

ProtoTest scopes its context per async flow, so NUnit's parallelism works. The sample suite runs fully parallel:

```csharp
[assembly: LevelOfParallelism(8)]
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
```

Each test gets its own context and its own lane in the ProtoTrace run timeline.

## Limits

- `IWrapSetUpTearDown` is synchronous, so the host calls block — NUnit needs a synchronizing context.
- `[SetUpFixture]` scoping is NUnit's own namespace rule; keep tests in or under the namespace of the setup class.
- A skipped test is only reported by NUnit; ProtoTest records nothing for it.
- Auto-wrap is assembly-wide and follows NUnit's nearest-wrapper rule: a fixture-level `IWrapSetUpTearDown` attribute other than `[ProtoTest]` suppresses it for that fixture, exactly as it would for `[ProtoTest]`.

## Next

- [Test runners](./overview.md) — the same setup for the other four runners.
- [Skip conditions](../foundation/skip-conditions.md) — the conditions every adapter evaluates.
