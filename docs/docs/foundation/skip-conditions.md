---
sidebar_position: 9
title: Skip conditions
description: "Skip a test before its lifecycle starts, with a reason, when the environment cannot run it, so it reads as skipped instead of failed."
---

# Skip conditions

Not every test can run in every environment. A **skip condition** stops a test before its lifecycle starts and hands the runner a reason, so an environment-specific test reads as *skipped* instead of *failed*.

A condition is just a `ProtoAttribute`:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Store, Reason = "The suite does not own the store.")]
```

Conditions are evaluated with the test's other attributes, and an adapter that doesn't know them simply runs the test — the contract is opt-in.

## Requiring a capability

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class RequiresCapabilityAttribute : ProtoAttribute, IProtoSkipCondition
{
    public RequiresCapabilityAttribute(string kind);

    public string Kind { get; }
    public string? CapabilityName { get; init; }
    public string? Reason { get; init; }
}
```

The test runs when `ProtoHost.HasCapability(kind, CapabilityName)` is true. Otherwise it skips with `Reason`, or with *"This test requires the '…' capability, which this host is not composed with."* when no reason is given:

```csharp
[RequiresCapability(
    ProtoCapabilityKinds.Broker,
    Reason = "No broker is configured; set ProtoTest:Messaging:RabbitMq:ConnectionString.")]
```

Integrations register a capability when they are configured, so the condition answers what the host *can actually do* rather than what it was asked to do. `ProtoCapabilityKinds` lists the built-in kinds — `server`, `protocol`, `store`, `broker`, `data`, `document` — and an integration may use its own. Use `CapabilityName` to require one specific capability of that kind:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Server, CapabilityName = "Northstar standalone")]
```

Class-level and method-level conditions accumulate like any other attribute; the first one that applies supplies the reason.

## In-process only

`[RequiresInProcess]` is shorthand for `[RequiresCapability(ProtoCapabilityKinds.Server)]`: it skips unless a `server` capability is registered, with the reason *"This test requires an in-process application server; the suite is running against a published environment."*

```csharp
[RequiresInProcess]
```

Because `"server"` is a capability *kind* and not a hosting mode, any registered server capability satisfies it. A suite that registers a `server` capability for something other than the in-process application — a standalone copy it started itself — will run the test too. Require a `CapabilityName` when only a particular server may satisfy the condition.

## Writing your own

Derive from `ProtoAttribute` and implement `IProtoSkipCondition`:

```csharp
public interface IProtoSkipCondition
{
    /// <summary>Returns the reason to skip, or null when the test can run.</summary>
    string? GetSkipReason(ProtoHost host);
}
```

`GetSkipReason` is called once per test, before the lifecycle starts; return `null` to let the test run.

## What a skip means

Adapters evaluate the conditions before calling `StartTestAsync`:

- the test body never runs;
- no `ProtoExecutionContext` is created, so no hook or attribute sees the test;
- nothing is written to the trace — no test record, no entries;
- no teardown runs, because there is nothing to tear down.

The reason is handed to the runner's skip mechanism, with one exception:

| Runner | How the skip is raised | Reason reported |
| --- | --- | --- |
| [NUnit](../runners/nunit.md) | `Assert.Ignore(reason)` | yes |
| [xUnit v2](../runners/xunit.md) with `[ProtoTestFact]` and `[ProtoTestTheory]` | `TestSkipped(Test, reason)` | yes |
| [xUnit v3](../runners/xunit3.md) | `Assert.Skip(reason)` | yes |
| [TUnit](../runners/tunit.md) | `TUnit.Core.Skip.Test(reason)` | yes |
| [MSTest](../runners/mstest.md) | an ignored `TestResult` | **no** |

MSTest has no dynamic skip API in the version ProtoTest targets, so the adapter can only return an ignored result carrying the method name. The reason is dropped — the test is still skipped, but the runner's output won't say why.

:::note[Skipped tests are absent, not empty]
Because nothing starts, a skipped test has no context, no trace record and no report entry. It appears in the runner's own results as skipped and nowhere in ProtoTest's output — the [runner overview](../runners/overview.md) describes the same rule from the outcome side.
:::

## Limits

- **The obsolete xUnit v2 `[Fact]` + `[ProtoTest]` style doesn't evaluate conditions.** `ProtoTestAttribute` is a `BeforeAfterTestAttribute` that starts the lifecycle directly, with no skip check. Use `[ProtoTestFact]` / `[ProtoTestTheory]`.
- **A condition only sees registered capabilities.** If an integration isn't configured, its capability is absent and tests that require it skip — which is the point. Configure the integration (or provide the connection string it reads) to make them run.
- **`[RequiresInProcess]` doesn't inspect `BaseUrl`.** With no in-process server registered it skips, regardless of what the host can reach over the network.
- **The reason is fixed at compile time.** Attribute arguments are constants; build messages from a name or a configuration key, as the examples do.

## In the sample suite

The demo gates each environment-dependent journey with a condition:

- `DomainAccessJourney` requires `ProtoCapabilityKinds.Store`, because composing the test-side domain needs a store the suite can connect to;
- `WebJourney` requires the named `"Northstar standalone"` server, because only the standalone instance gives browser tests an address;
- `MessagingJourney` requires `ProtoCapabilityKinds.Broker`, and the broker capability only exists when a real broker adapter is configured.

`[RequiresInProcess]` itself is exercised by the repository's own NUnit tests (`tests/ProtoTest.NUnit.Tests/SkipConditionTests.cs`).
