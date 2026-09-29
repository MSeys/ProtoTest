---
sidebar_position: 4
title: NUnit
description: "Register ProtoTest with NUnit: the SetUpFixture, the [ProtoTest] attribute, what the adapter changes and its limits."
---

# NUnit

`ProtoTest.NUnit` starts the host from a `[SetUpFixture]` and wraps each `[ProtoTest]` method in a ProtoTest context. The wrapper sits outside NUnit's setup and teardown, so the lifecycle spans `[SetUp]`, the body and `[TearDown]`. The repository's own sample suite uses this adapter.

## Install

```bash
dotnet add package ProtoTest.NUnit
```

ProtoTest targets **.NET 8, 9 and 10**, and needs **NUnit 4.6.1 or newer**; the standard `dotnet new nunit` template pins an older version, so update NUnit first: `dotnet add package NUnit --version 4.6.1`. The `dotnet new prototest` template defaults to `net10.0`; pass `-f net8.0` or `-f net9.0` for an older runtime.

## Register

`ProtoTestAssembly` carries `[SetUpFixture]` and owns `[OneTimeSetUp]` and `[OneTimeTearDown]`. Derive from it and configure the host:

```csharp
[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder.AddApplication("Api", app => app
            .AddRest(rest => rest.AddClient("Api")));
}
```

The test attribute is `[ProtoTest]`. It derives from NUnit's `TestAttribute`, so it replaces `[Test]`:

```csharp
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

:::warning[Namespace scoping]
This is NUnit's own rule. A `[SetUpFixture]` outside any namespace applies to the whole assembly, while one inside a namespace applies only to that namespace and its children. If tests in another namespace cannot find the host, that is usually why.
:::

**Low-ceremony mode.** Add `[assembly: ProtoTestAutoWrap]` and every plain `[Test]` runs through the same lifecycle. NUnit applies the nearest `IWrapSetUpTearDown` attribute (method, then fixture, then assembly), so a test that carries `[ProtoTest]` keeps its own wrapper and is never wrapped twice.

## What the adapter changes

| Item | What the adapter does |
| --- | --- |
| The host | `ProtoTestAssembly` already carries `[SetUpFixture]` and starts and stops the one host for the assembly. |
| The test attribute | `[ProtoTest]` replaces `[Test]`. It implements NUnit's `IWrapSetUpTearDown`, which is how the lifecycle wraps `[SetUp]`, the body and `[TearDown]`. |
| The lifecycle | The wrapper resolves the method's attributes and skip conditions, starts the context, then runs NUnit's own test command. It maps the result NUnit recorded and completes the context in a `finally`. A setup failure rolls back and fails the test. |
| Scheduling | Both calls block on the host task (`GetAwaiter().GetResult()`), so NUnit needs a synchronizing context. |
| Cancellation | The adapter passes `TestExecutionContext.CancellationToken`, which `[CancelAfter]` cancels. The body reads it as `Proto.Context.CancellationToken`. |
| Outcomes | `Passed` to `Passed`, `Failed` to `Failed`, `Skipped` to `Skipped`, `Inconclusive` to `Skipped`, `Warning` to `Partial`. Anything else is `Unknown`. |
| Failure detail | A body exception that escapes the command is kept unwrapped and recorded with its real type, message and stack. When NUnit records the failure itself, the type is `NUnit.{Label}`, or `NUnit.Failed` when NUnit reports no label. NUnit exposes no exception type, so a runner-cancelled test reads as `Failed`. |
| Skips | The conditions run before anything else, not even `[SetUp]`. A skip reports an ignored result (`ResultState.Ignored`), and the reason is reported as given. |
| Attachments | `TestContext.AddTestAttachment(path, description)`, using the attachment's description or its name. |
| Test names | NUnit's name for the case: the fully qualified method name, with the row's arguments for a parameterized test. |
| Auto-wrap | Optional. `[assembly: ProtoTestAutoWrap]` wraps every plain `[Test]`, and the nearest-wrapper rule keeps an explicit `[ProtoTest]` in charge. |

## Limits

- `IWrapSetUpTearDown` is synchronous, so the host calls block. NUnit needs a synchronizing context.
- `[SetUpFixture]` scoping is NUnit's namespace rule. Keep tests in or under the namespace of the setup class.
- A skipped test is reported only by NUnit. ProtoTest records nothing for it.
- NUnit's result carries no exception, so the adapter cannot tell a cancelled test from a failed one. A runner-cancelled test reads as `Failed`.
- Auto-wrap follows NUnit's nearest-wrapper rule. A fixture-level `IWrapSetUpTearDown` attribute other than `[ProtoTest]` suppresses it for that fixture, exactly as it would suppress `[ProtoTest]`.
- Parallel execution works because ProtoTest scopes its context per async flow. See [Concurrency](../foundation/concurrency.md).

## Learn more

- [Test runners](./overview.md): the five adapters side by side.
- [Skip conditions](../foundation/skip-conditions.md): the conditions every adapter evaluates.
- [Lifecycle](../foundation/lifecycle.md): the hooks and attributes around a test.
