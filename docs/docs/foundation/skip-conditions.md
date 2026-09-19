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

Integrations register a capability when they are configured, so the condition answers what the host *can actually do* rather than what it was asked to do. `ProtoCapabilityKinds` lists the built-in kinds — `server`, `protocol`, `browser`, `store`, `broker`, `data`, `document` — and an integration may use its own. Use `CapabilityName` to require one specific capability of that kind:

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

## Requiring a Playwright browser

`AddWeb(...)` registers a `browser` capability named `Playwright` (or `Selenium`), so `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Playwright")]` proves the backend is composed — it says nothing about whether a browser actually exists on the machine. `ProtoTest.Web.Playwright` ships a stronger, opt-in condition that probes the browser before the lifecycle starts, without launching one:

```csharp
[RequiresPlaywrightBrowser]                                   // the configured browser
[RequiresPlaywrightBrowser(browser: PlaywrightBrowser.Firefox)]
[RequiresPlaywrightBrowser(channel: "msedge", Reason = "No Edge in this environment.")]
[RequiresPlaywrightBrowser(Session = "Admin")]                // the Admin session's options
public async Task ...() { ... }
```

The condition reads the host's Playwright options — your `AddWeb(...)` callback supplies the defaults, configuration overrides them — and returns:

- **nothing to skip** when `InstallBrowsers` is true — the backend pool downloads the browser on demand before its first launch;
- **nothing to skip** when the configured bundled browser's executable exists; the probe starts the Playwright driver (no browser process is launched) and reads `BrowserType.ExecutablePath`;
- **nothing to skip** for a channel Playwright recognizes (`chrome`, `msedge`, …) — a channel names a system browser, which only a real launch can resolve, so the condition cannot prove one absent; an unrecognized channel does skip;
- **a reason naming Playwright** otherwise, pointing at the install options (`playwright.ps1 install …`, `InstallBrowsers=true`, or a `Channel`).

Set `Session = "Admin"` when the test drives a named session: the probe then merges that session's `ProtoTest:Web:Sessions:Admin` section over `ProtoTest:Web:Playwright`, exactly like the backend binds, so a session-level browser, channel or install-browsers setting never disagrees with the launch.

Like any condition, `Reason` replaces that default message.

Selenium has no equivalent probe: the driver comes from your own factory, so the framework cannot know whether a browser exists. Gate Selenium tests with `[RequiresCapability(ProtoCapabilityKinds.Browser, CapabilityName = "Selenium")]` and a try/catch around the first session that uses the browser, calling your runner's skip mechanism:

```csharp
try
{
    var home = Proto.Context.Web().Page<HomePage>();
    await home.OpenAsync("/");
}
catch (Exception exception) when (exception is WebDriverException or InvalidOperationException)
{
    Assert.Ignore($"No Selenium browser is available: {exception.Message}");
}
```

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

The reason is handed to the runner's skip mechanism:

| Runner | How the skip is raised | Reason reported |
| --- | --- | --- |
| [NUnit](../runners/nunit.md) | `Assert.Ignore(reason)` | yes |
| [xUnit v2](../runners/xunit.md) with `[ProtoTestFact]` and `[ProtoTestTheory]` | the discovered test case's `SkipReason` | yes |
| [xUnit v3](../runners/xunit3.md) | `Assert.Skip(reason)` | yes |
| [TUnit](../runners/tunit.md) | `TUnit.Core.Skip.Test(reason)` | yes |
| [MSTest](../runners/mstest.md) | an ignored `TestResult` | yes, on its `DisplayName` and `LogOutput` |

MSTest has no public dynamic skip API in the version ProtoTest targets, so the adapter returns an ignored result instead. Its `IgnoreReason` is internal, so the reason travels on the public `LogOutput` and is also prefixed to the display name — the test reads as skipped and the runner's output still says why.

:::note[Skipped tests are absent, not empty]
Because nothing starts, a skipped test has no context, no trace record and no report entry. It appears in the runner's own results as skipped and nowhere in ProtoTest's output — the [runner overview](../runners/overview.md) describes the same rule from the outcome side.
:::

## Limits

- **A condition only sees registered capabilities.** If an integration isn't configured, its capability is absent and tests that require it skip — which is the point. Configure the integration (or provide the connection string it reads) to make them run.
- **`[RequiresInProcess]` doesn't inspect `BaseUrl`.** With no in-process server registered it skips, regardless of what the host can reach over the network.
- **The reason is fixed at compile time.** Attribute arguments are constants; build messages from a name or a configuration key, as the examples do.
- **Conditions are selected in resolution order, not `Order`.** `ProtoTestSkip.GetReason` returns the first non-null `GetSkipReason` in the order the adapter resolved the attributes — for NUnit, class-level before method-level in reflection order — because ordering by `Order` only happens inside `StartTestAsync`, which a skipped test never reaches.

## In the sample suite

The demo gates each environment-dependent journey with a condition:

- `DomainAccessJourney` requires `ProtoCapabilityKinds.Store`, because composing the test-side domain needs a store the suite can connect to;
- `WebJourney` requires the named `"Northstar standalone"` server, because only the standalone instance gives browser tests an address;
- `MessagingJourney` requires `ProtoCapabilityKinds.Broker`, and the broker capability only exists when a real broker adapter is configured.

`[RequiresInProcess]` itself is exercised by the repository's own NUnit tests (`tests/ProtoTest.NUnit.Tests/SkipConditionTests.cs`).
