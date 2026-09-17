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
        builder.AddRest(rest => rest.AddClient("Api", "https://api.example.test/"));
}
```

:::warning Namespace scoping
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
public class OrderTests
{
    [ProtoTest]
    public async Task Orders_endpoint_responds()
    {
        var response = await Proto.Context.Rest("Api").GetAsync("/api/orders");
        response.ShouldHaveStatus(HttpStatusCode.OK);
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

**Test names are fully qualified.** NUnit is the only runner where ProtoTest records `test.FullName` rather than the bare method name, so trace and report entries read `Namespace.OrderTests.Orders_endpoint_responds`.
