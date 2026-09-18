---
sidebar_position: 4
title: NUnit
---

# NUnit

```bash
dotnet add package ProtoTest.NUnit
```

This is the runner the repository's own sample suite uses.

## Assembly setup

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

## Writing a test

`[ProtoTest]` derives from `TestAttribute`, so it **replaces** `[Test]`.

```csharp
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;
using System.Net;

[TestFixture]
[Application("Api")]
public class OrderTests
{
    [ProtoTest]
    public async Task Orders_endpoint_responds()
    {
        var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.ShouldHaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## Parallel execution

ProtoTest scopes its context per async flow, so NUnit's parallelism works. The sample suite runs fully parallel:

```csharp
[assembly: LevelOfParallelism(8)]
[assembly: Parallelizable(ParallelScope.All)]
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
```

Each test gets its own context and its own lane in the ProtoTrace run timeline.

## Things to know

**Outcomes are recorded**, mapped from `TestContext.CurrentContext.Result.Outcome.Status`, with the error type reported as `NUnit.{Label}`.

**Attachments** go to `TestContext.AddTestAttachment(path, description)`.

**Test names are fully qualified.** Every adapter except xUnit v2 records `ProtoTestName.FromMethod`, which produces `DeclaringType.FullName.MethodName`, so trace and report entries read `Namespace.OrderTests.Orders_endpoint_responds` on NUnit, MSTest, xUnit v3 and TUnit alike. xUnit v2 records xUnit's display name instead, so theory rows stay distinguishable (`…Invoices_filter_by_state(state: "open")`).
