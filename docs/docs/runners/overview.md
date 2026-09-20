---
sidebar_position: 1
title: .NET test runners
sidebar_label: Overview
description: "ProtoTest plugs into the test runner you already use — NUnit, xUnit v2 and v3, MSTest or TUnit — and behaves the same on each."
---

# Test runners

ProtoTest doesn't replace your test runner. It plugs into the one you already use and wraps each test in a `ProtoExecutionContext`, so everything in [Foundation](../foundation/overview.md) works identically no matter which runner you pick.

ProtoTest targets **.NET 8, 9 and 10**, and every adapter package is stable on NuGet: install it with `dotnet add package <id>`.

| Runner | Package | Test attribute | Assembly setup |
| --- | --- | --- | --- |
| [xUnit v2](./xunit.md) | `ProtoTest.Xunit` | `[ProtoTestFact]` / `[ProtoTestTheory]` | a collection fixture |
| [xUnit v3](./xunit3.md) | `ProtoTest.Xunit3` | `[ProtoTestFact]` / `[ProtoTestTheory]` | `[assembly: AssemblyFixture]` |
| [NUnit](./nunit.md) | `ProtoTest.NUnit` | `[ProtoTest]` (replaces `[Test]`) | `[SetUpFixture]` |
| [MSTest](./mstest.md) | `ProtoTest.MSTest` | `[ProtoTest]` (replaces `[TestMethod]`) | `[AssemblyInitialize]` / `[AssemblyCleanup]` |
| [TUnit](./tunit.md) | `ProtoTest.TUnit` | `[Test]` (TUnit's own) | `[assembly: TestExecutor<ProtoTestExecutor>]` |

## The two pieces

Whichever runner you use, you write the same two things.

**1. An assembly setup class** deriving from that package's `ProtoTestAssembly`. It builds the `ProtoHost` once per test process and exposes it as a static `Host`:

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api")));
```

**2. A test attribute** — or, for TUnit, the registered executor — that starts the execution context before your method body and completes it afterwards. Tests select the application with `[Application("Api")]`, and the protocol accessors (`Proto.Context.Rest()`, `Proto.Context.GraphQL()`, `Proto.Context.Web()`) then use the clients bound to it.

:::note[One host per process]
`Host` is a static member inside each runner package — there is one host per test process. Touching `Proto.Context` before the assembly setup has run throws an `InvalidOperationException` naming the setup class the runner expects.
:::

## Per-test lifecycle

Before the body the adapter calls `StartTestAsync`: the context is created, the test's hooks and attributes run — clients and test-scoped resources are initialized here — and `test.execution` opens. After the body it calls `CompleteTestAsync` with the outcome the runner recorded, and the context is torn down. Those are the trace's `test.setup`, `test.execution` and `test.teardown` entries. A setup failure is rolled back and reported as a failure; a skipped test never starts, so it has no context and no trace entry.

xUnit v2, MSTest and TUnit run this lifecycle asynchronously. NUnit and xUnit v3 call the host synchronously (`GetAwaiter().GetResult()`), so they need a synchronizing context.

## Outcomes

| Runner | Outcomes ProtoTest records | Why |
| --- | --- | --- |
| xUnit v2 | Passed, Failed, Cancelled | xUnit's aggregator decides pass/fail; cancellation is read from the runner's `CancellationTokenSource`; a failure keeps the exception. |
| xUnit v3 | Passed, Failed, Skipped, Unknown | Skipped covers xUnit's `Skipped` and `NotRun`; Unknown is the fallback for an unmapped state. |
| NUnit | Passed, Failed, Skipped, Partial, Unknown | Inconclusive maps to Skipped; Warning maps to Partial because the test passed with warnings attached. |
| MSTest | Passed, Failed, Skipped, Partial, Unknown | Ignored and Inconclusive map to Skipped; a mix of passed and ignored data rows maps to Partial; Unknown when the result is empty or unmapped. |
| TUnit | Passed, Failed, Skipped, Cancelled | `SkipTestException` maps to Skipped, `OperationCanceledException` to Cancelled; anything else fails. |

Failure detail: xUnit v2 carries the exception into the trace; xUnit v3 records the state's exception type, message and stack; NUnit reports the type as `NUnit.{Label}` (`NUnit.Failed` when the label is empty); MSTest uses `MSTest.{Outcome}` when the result has no failure exception; TUnit records the thrown exception.

A skipped test appears only in the runner's own results. Nothing is written to ProtoTest's trace or reports for it — no record, no teardown.

## Skip conditions

All adapters evaluate [`[RequiresCapability]`, `[RequiresInProcess]` and `[RequiresPlaywrightBrowser]`](../foundation/skip-conditions.md) before `StartTestAsync`, so a skipped test has no context, no trace entry and no teardown. The reason reaches the runner:

| Runner | How the skip is raised | Reason reported |
| --- | --- | --- |
| NUnit | `Assert.Ignore(reason)` | as-is |
| xUnit v2 | xUnit's `SkipReason` | as-is |
| xUnit v3 | `Assert.Skip(reason)` | as-is |
| TUnit | `TUnit.Core.Skip.Test(reason)` | as-is |
| MSTest | an ignored `TestResult` | on the display name and `LogOutput` |

MSTest has no public dynamic-skip API in the version ProtoTest targets, so the adapter returns an ignored result and the reason travels on `LogOutput` and the display name.

## Attachments

Artifacts ProtoTest captures (request/response bodies, screenshots, Playwright traces) are handed to the runner so they appear in its own reporting:

| Runner | How |
| --- | --- |
| NUnit | `TestContext.AddTestAttachment(path, description)` |
| xUnit v3 | `TestContext.Current.AddAttachment(name, bytes, mediaType)` |
| MSTest | appended to the **first** data-row result's `TestResult.ResultFiles` |
| TUnit | `context.Output.AttachArtifact(path, name, description)` |
| xUnit v2 | materialized to a file, with the path written to the console — v2 has no attachment API |

## Test names

Every adapter except xUnit v2 records `ProtoTestName.FromMethod`, which produces `DeclaringType.FullName.MethodName`. xUnit v2 records xUnit's display name instead, so theory rows stay distinguishable (`…Invoices_filter_by_state(state: "open")`).

## Custom attributes work everywhere

Every runner resolves `ProtoAttribute` subclasses through the same `ProtoAttributeResolver`, so capabilities you write once — see [Attributes](../foundation/attributes.md) — behave identically across all five.
