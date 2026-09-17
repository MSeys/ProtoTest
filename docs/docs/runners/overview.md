---
sidebar_position: 1
title: Overview
---

# Test runners

ProtoTest doesn't replace your test runner. It plugs into the one you already use and wraps each test in a `ProtoExecutionContext`, so everything in [Foundation](../foundation/overview) works identically no matter which runner you pick.

| Runner | Package | Test attribute | Assembly setup |
| --- | --- | --- | --- |
| [xUnit v2](./xunit) | `ProtoTest.Xunit` | `[Fact]` **+** `[ProtoTest]` | Collection fixture |
| [xUnit v3](./xunit3) | `ProtoTest.Xunit3` | `[ProtoTestFact]` / `[ProtoTestTheory]` | `[assembly: AssemblyFixture]` |
| [NUnit](./nunit) | `ProtoTest.NUnit` | `[ProtoTest]` (replaces `[Test]`) | `[SetUpFixture]` |
| [MSTest](./mstest) | `ProtoTest.MSTest` | `[ProtoTest]` (replaces `[TestMethod]`) | `[AssemblyInitialize]` |
| [TUnit](./tunit) | `ProtoTest.TUnit` | `[Test]` (TUnit's own) | `[assembly: TestExecutor<…>]` |

## The two pieces

Whichever runner you use, you write the same two things.

**1. An assembly setup class** deriving from that package's `ProtoTestAssembly`. It builds the `ProtoHost` once per test process and exposes it as a static `Host`:

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder.AddRest(rest => rest.AddClient("Api", "https://api.example.test/"));
```

**2. A test attribute** that starts the execution context before your method body and completes it afterwards.

:::note One host per process
`Host` is a static field inside each runner package — there is one host per test process. Touching `Proto.Context` before the assembly setup has run throws an `InvalidOperationException` telling you which setup class is missing.
:::

## How much of the result ProtoTest sees

The runners differ in how much outcome information they can hand back, which shows up in your trace and reports:

| Runner | Recorded outcome |
| --- | --- |
| xUnit v3, NUnit, MSTest | Passed / Failed / Skipped, with the exception |
| TUnit | Passed / Failed / **Cancelled**, with the exception |
| xUnit v2 | Always `Unknown` — v2 exposes no outcome to a `BeforeAfterTestAttribute` |

If a precise pass/fail outcome in ProtoTrace matters to you, prefer xUnit v3 over v2.

## Attachments

Artifacts ProtoTest captures (request/response bodies, screenshots, Playwright traces) are handed to the runner so they appear in its own reporting:

| Runner | How |
| --- | --- |
| NUnit | `TestContext.AddTestAttachment` |
| xUnit v3 | `TestContext.Current.AddAttachment` |
| MSTest | appended to `TestResult.ResultFiles` |
| TUnit | `context.Output.AttachArtifact` |
| xUnit v2 | written to disk, path written to the console (v2 has no attachment API) |

## Custom attributes work everywhere

Every runner resolves `ProtoAttribute` subclasses through the same `ProtoAttributeResolver`, so capabilities you write once — see [Attributes](../foundation/attributes) — behave identically across all five.
