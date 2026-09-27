---
sidebar_position: 3
title: xUnit v3
description: "Set up ProtoTest with xUnit v3: the assembly fixture, [ProtoTestFact] tests, outcomes and native attachments."
---

# xUnit v3

`ProtoTest.Xunit3` uses xUnit v3's assembly fixture and its `IBeforeAfterTestAttribute` hooks, so each test gets a ProtoTest context and artifacts go through xUnit's native attachment API.

## Install

```bash
dotnet add package ProtoTest.Xunit3
```

ProtoTest targets **.NET 8, 9 and 10**. The `dotnet new prototest` template defaults to `net10.0`; pass `-f net8.0` or `-f net9.0` for an older runtime.

## Enable it

Register the setup class with `[assembly: AssemblyFixture(...)]` — no collection needed, and it applies to every test in the assembly.

```csharp
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit3;
using Xunit;

[assembly: AssemblyFixture(typeof(Setup))]

public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api")));
}
```

Without the assembly fixture the host is never initialized, and `ProtoTestAssembly.Host` throws `InvalidOperationException` telling you to register it.

## Quick start

`[ProtoTestFact]` and `[ProtoTestTheory]` derive from `FactAttribute` and `TheoryAttribute`, so they replace them outright. Both implement xUnit v3's `IBeforeAfterTestAttribute`, and their `Before`/`After` run around every test case.

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit3;
using Xunit;

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

A `[ProtoTestTheory]` behaves the same way; each `[InlineData]` row is a test of its own and is recorded under xUnit's display name, with the arguments included, so the rows stay apart.

## Low-ceremony mode (opt-in)

A suite can skip `[ProtoTestFact]`/`[ProtoTestTheory]` on every method: apply the assembly-level auto-wrap once, and every plain `[Fact]` and `[Theory]` runs through the same lifecycle.

```csharp
[assembly: ProtoTestAutoWrap]
```

A test that already carries `[ProtoTestFact]` or `[ProtoTestTheory]` keeps its own handler and is never wrapped twice. The skip conditions, outcome mapping and attachments are the ones above, unchanged.

## Per-test lifecycle

`Before` resolves the test's attributes and conditions, then calls `StartTestAsync`; `After` reads `TestContext.Current.TestState` and completes the context with the mapped result. The lifecycle handler calls the host synchronously (`.GetAwaiter().GetResult()`), so a synchronizing context is required - the same reason NUnit blocks.

The context starts in the before-attribute, which xUnit v3 runs **after** class construction and `IAsyncLifetime.InitializeAsync` and **before** class disposal. `Proto.Context` is therefore unavailable in a test class's constructor and `InitializeAsync`, and class cleanup runs after the context completes. If a later `IBeforeAfterTestAttribute` throws, xUnit skips the after methods and the test's trace stays open; the run-level contract check reports it at teardown instead of letting it pass silently.

## Outcomes

| xUnit state | ProtoTest records | Why |
| --- | --- | --- |
| `Passed` | `Passed` | |
| `Skipped`, `NotRun` | `Skipped` | a body-level `Assert.Skip` is a state, so it still lands on a started context |
| `Failed` | `Failed` | with the exception type, message and stack trace |
| `Failed` with an OCE/TaskCanceledException type | `Cancelled` | the test was interrupted, not broken |
| anything else | `Unknown` | the state has no ProtoTest equivalent |

## Skipping

Before the lifecycle starts, `ProtoTestSkip.GetReason` resolves the test's attributes; a skip calls `Assert.Skip(reason)` and nothing is started, so no trace entry is written. A body-level `Assert.Skip` is different: the context already exists and maps to `Skipped` through the test state. See [Skip conditions](../foundation/skip-conditions.md).

## Attachments

Artifacts go through xUnit's own API — `TestContext.Current.AddAttachment(name, bytes, replaceExistingValue: false, mediaType)` — and appear with the test in xUnit's output.

## Limits

- The lifecycle handler is synchronous-over-async, so xUnit v3 needs a synchronizing context (xUnit v2, MSTest and TUnit do not).
- The attributes are `AllowMultiple = false`, like the `Fact` and `Theory` attributes they replace.
- The context starts in the before-attribute: class construction, `IAsyncLifetime.InitializeAsync` and class disposal are outside it.
- A throwing sibling `IBeforeAfterTestAttribute` prevents xUnit from running the after methods; the run's contract check reports the unfinished test at teardown.
- The assembly fixture is mandatory; there is no collection-level variant.
- Auto-wrap is assembly-wide: the attribute is declared on the assembly, and there is no per-class opt-in.

## Next

- [Test runners](./overview.md) — the same setup for the other four runners.
- [Skip conditions](../foundation/skip-conditions.md) — the conditions every adapter evaluates.
