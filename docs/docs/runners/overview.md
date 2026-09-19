---
sidebar_position: 1
title: Overview
description: "ProtoTest plugs into the test runner you already use — NUnit, xUnit v2 and v3, MSTest or TUnit — and behaves the same on each."
---

# Test runners

ProtoTest doesn't replace your test runner. It plugs into the one you already use and wraps each test in a `ProtoExecutionContext`, so everything in [Foundation](../foundation/overview) works identically no matter which runner you pick.

| Runner | Package | Test attribute | Assembly setup |
| --- | --- | --- | --- |
| [xUnit v2](./xunit.md) | `ProtoTest.Xunit` | `[ProtoTestFact]` / `[ProtoTestTheory]` | Collection fixture |
| [xUnit v3](./xunit3.md) | `ProtoTest.Xunit3` | `[ProtoTestFact]` / `[ProtoTestTheory]` | `[assembly: AssemblyFixture]` |
| [NUnit](./nunit.md) | `ProtoTest.NUnit` | `[ProtoTest]` (replaces `[Test]`) | `[SetUpFixture]` |
| [MSTest](./mstest.md) | `ProtoTest.MSTest` | `[ProtoTest]` (replaces `[TestMethod]`) | `[AssemblyInitialize]` |
| [TUnit](./tunit.md) | `ProtoTest.TUnit` | `[Test]` (TUnit's own) | `[assembly: TestExecutor<…>]` |

## The two pieces

Whichever runner you use, you write the same two things.

**1. An assembly setup class** deriving from that package's `ProtoTestAssembly`. It builds the `ProtoHost` once per test process and exposes it as a static `Host`:

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api")));
```

**2. A test attribute** that starts the execution context before your method body and completes it afterwards. Tests select the application with `[Application("Api")]`, and the protocol accessors (`Proto.Context.Rest()`, `Proto.Context.GraphQL()`, `Proto.Context.Web()`) then use the clients bound to it.

:::note[One host per process]
`Host` is a static field inside each runner package — there is one host per test process. Touching `Proto.Context` before the assembly setup has run throws an `InvalidOperationException` telling you which setup class is missing.
:::

## How much of the result ProtoTest sees

The runners differ in how much outcome information they can hand back, which shows up in your trace and reports:

| Runner | Recorded outcome |
| --- | --- |
| xUnit v3, NUnit, MSTest | Passed / Failed / Skipped, with the exception |
| xUnit v2 with `[ProtoTestFact]` and `[ProtoTestTheory]` | Passed / Failed / Cancelled, with the exception |
| TUnit | Passed / Failed / Cancelled, with the exception |
| xUnit v2 with `[Fact]` + `[ProtoTest]` | Always `Unknown` — see [xUnit v2](./xunit.md#the-older-fact--prototest-style) |

## Skip conditions

A test can declare what it needs and skip when the host doesn't have it. All adapters evaluate [`[RequiresCapability]` and `[RequiresInProcess]`](../foundation/skip-conditions.md) before `StartTestAsync` — so a skipped test has no context, no trace entry and no teardown. The reason reaches NUnit, xUnit v3, TUnit and the xUnit v2 `[ProtoTestFact]` / `[ProtoTestTheory]` attributes; MSTest can only return an ignored result without a message. The obsolete xUnit v2 `[Fact]` + `[ProtoTest]` style doesn't evaluate conditions at all. See [Skip conditions](../foundation/skip-conditions.md) for the exact per-runner behaviour.

## Attachments

Artifacts ProtoTest captures (request/response bodies, screenshots, Playwright traces) are handed to the runner so they appear in its own reporting:

| Runner | How |
| --- | --- |
| NUnit | `TestContext.AddTestAttachment` |
| xUnit v3 | `TestContext.Current.AddAttachment` |
| MSTest | appended to the first data-row result's `TestResult.ResultFiles` |
| TUnit | `context.Output.AttachArtifact` |
| xUnit v2 | written to disk, path written to the console (v2 has no attachment API) |

## Custom attributes work everywhere

Every runner resolves `ProtoAttribute` subclasses through the same `ProtoAttributeResolver`, so capabilities you write once — see [Attributes](../foundation/attributes) — behave identically across all five.
