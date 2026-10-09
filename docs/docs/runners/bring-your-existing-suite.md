---
sidebar_position: 7
title: Bring an existing suite
description: "Move an existing suite onto ProtoTest test by test: what keeps running, what converts, and the order that stays green for xUnit, NUnit, MSTest and TUnit."
---

import TabbedCode from '@site/src/components/TabbedCode';

# Bring an existing suite

Convert one class at a time. Plain tests keep running. The xUnit runbook below is the shape. NUnit, MSTest and TUnit follow it with their own host hook and attribute swap.

## What stays

- Plain `[Fact]` and `[Theory]` tests keep running unchanged, whether their class is converted or not. They stay outside the trace.
- xUnit's discovery, ordering, parallelism and theory rows stay xUnit's. ProtoTest adds the per-test context and the trace around them.
- A converted test keeps its arrange/act/assert body. The composition moves into the host.

## The order that stays green

- [ ] **1. Add the package** (`ProtoTest.Xunit` or `ProtoTest.Xunit3`) plus the integration packages the tests use. Gate: the solution builds.
- [ ] **2. Write the host once**: the fixture composes applications, clients and infrastructure. Gate: untouched tests still pass.
- [ ] **3. Convert one class**: replace `[Fact]` with `[ProtoTestFact]`, add the collection on v2, and read `Proto.Context` in the body. Gate: converted tests get a trace.
- [ ] **4. Run `dotnet test`.** Gate: green.
- [ ] **5. Delete the per-class harness** the converted tests no longer need. Gate: no test shares a fixed row, tenant or file (see [Concurrency](../foundation/concurrency.md)).

:::danger[Step 3 on v2: the collection attribute is mandatory]
A converted v2 class without `[Collection(ProtoTestCollection.Name)]` fails that ProtoTest test with `ProtoHost is not initialized`, naming the missing collection fixture. The other tests still report a result and the run fails. Add the attribute in the same change.
:::

:::warning[Step 4 on SDK 10 with v3: the MTP opt-in comes first]
An xUnit v3 project on .NET SDK 10 needs the Microsoft.Testing.Platform opt-in in `global.json` before `dotnet test` runs it. Run the command from that directory, or name the project from at or under it. From outside the `global.json` folder, `cd` there first. [xUnit v3](./xunit3.md) shows the file and the working commands.
:::

## Before and after, per version

<TabbedCode
  label="Converted test class, before and after"
  tabs={[
    {
      id: 'v2-before',
      label: 'v2 before',
      filename: 'OrderTests.cs (plain xUnit v2)',
      language: 'csharp',
      code: `[Fact]
public async Task Orders_endpoint_responds()
{
    using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
    using var response = await client.GetAsync("/api/orders");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}`,
    },
    {
      id: 'v2-after',
      label: 'v2 after',
      filename: 'OrderTests.cs (converted)',
      language: 'csharp',
      code: `[Collection(ProtoTestCollection.Name)]
[Application("Api")]
public class OrderTests
{
    [ProtoTestFact]
    public async Task Orders_endpoint_responds()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}`,
    },
    {
      id: 'v3-before',
      label: 'v3 before',
      filename: 'OrderTests.cs (plain xUnit v3)',
      language: 'csharp',
      code: `public class OrderTests
{
    private readonly HttpClient _client = new() { BaseAddress = new Uri("http://localhost:5000") };

    [Fact]
    public async Task Orders_endpoint_responds()
    {
        using var response = await _client.GetAsync("/api/orders");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}`,
    },
    {
      id: 'v3-after',
      label: 'v3 after',
      filename: 'OrderTests.cs (converted)',
      language: 'csharp',
      code: `[Application("Api")]
public class OrderTests
{
    [ProtoTestFact]
    public async Task Orders_endpoint_responds()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}`,
    },
  ]}
/>

What changed in both versions: the arrangement moves into the host, the port moves into configuration, and the assertion becomes a ProtoTest check. What differs: v2 needs the collection attribute on the class, and v3 moves context reads out of the constructor into the body.

| | xUnit v2 | xUnit v3 |
| --- | --- | --- |
| The host | `ProtoTestFixture : ProtoTestAssembly`, registered with `[CollectionDefinition]` | `Setup : ProtoTestAssembly`, registered with `[assembly: AssemblyFixture(typeof(Setup))]` |
| A test | `[ProtoTestFact]` / `[ProtoTestTheory]` replace `[Fact]` / `[Theory]` | the same, or keep `[Fact]` and add `[assembly: ProtoTestAutoWrap]` |
| A converted class | `[Collection(ProtoTestCollection.Name)]` is mandatory. Without it that ProtoTest test fails and names the missing collection fixture. | nothing extra, because the assembly fixture covers every class |
| The constructor | Already runs inside the context, so `Proto.Context` works there | Runs before the context. Move context reads into `IAsyncLifetime.InitializeAsync` or the test body. |
| Attachments | Files land under `%TEMP%\ProtoTest\attachments`, the path prints to the console with `--logger "console;verbosity=detailed"` | `TestContext.Current.AddAttachment(...)`, so artifacts appear with the test in xUnit's output |

The registration details, outcome mapping and limits are on [xUnit v2](./xunit.md) and [xUnit v3](./xunit3.md).

## NUnit: the order that stays green

The same five steps, with NUnit's host hook and attribute swap:

- [ ] **1. Add the package** (`ProtoTest.NUnit`) plus the integration packages the tests use. NUnit needs **4.6.1 or newer**, so update it first when you started from `dotnet new nunit`: it pins an older one, while `dotnet new prototest` already resolves 4.6.1. Gate: the solution builds.
- [ ] **2. Write the host once**: a `[SetUpFixture]` class deriving from `ProtoTestAssembly` with the `Configure` override. Keep it outside any namespace, or it covers only that namespace's subtree. The `dotnet new prototest` template keeps `Setup` inside the project namespace by design, beside the tests it generates; hoist it out once tests span namespaces. Gate: untouched tests still pass.
- [ ] **3. Convert one class**: replace `[Test]` with `[ProtoTest]` (it derives from NUnit's `TestAttribute`), keep `[TestFixture]`, and read `Proto.Context` in `[SetUp]`, the body or `[TearDown]`. Gate: converted tests get a trace.
- [ ] **4. Run `dotnet test`.** Gate: green.
- [ ] **5. Delete the per-class harness** the converted tests no longer need. Gate: no test shares a fixed row, tenant or file (see [Concurrency](../foundation/concurrency.md)).

Per-class pitfalls: `Proto.Context` works in `[SetUp]`, the body and `[TearDown]`, but not in the fixture's `[OneTimeSetUp]`, which builds the host. Or add `[assembly: ProtoTestAutoWrap]` and every plain `[Test]` runs through the same lifecycle without an attribute swap. An explicit `[ProtoTest]` still wins by NUnit's nearest-wrapper rule.

```csharp
// Before: per-class harness with its own client.
[TestFixture]
public class OrderTests
{
    private HttpClient _client = new() { BaseAddress = new Uri("http://localhost:5000") };

    [Test]
    public async Task Orders_endpoint_responds()
    {
        using var response = await _client.GetAsync("/api/orders");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}

// After: the arrangement lives in the host, the test reads the context.
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

The host hook, the context window and the namespace rule are on [NUnit](./nunit.md).

## MSTest: the order that stays green

The same five steps, with MSTest's assembly hooks and per-row attribute swap:

- [ ] **1. Add the package** (`ProtoTest.MSTest`) plus the integration packages the tests use. Gate: the solution builds.
- [ ] **2. Write the host once**: a `[TestClass]` deriving from `ProtoTestAssembly` whose `[AssemblyInitialize]` calls `InitializeAsync` and whose `[AssemblyCleanup]` calls `CleanupAsync`. Call `InitializeAsync` exactly once. Gate: untouched tests still pass.
- [ ] **3. Convert one class**: replace `[TestMethod]` with `[ProtoTest]` (it derives from `TestMethodAttribute`), keep `[TestClass]`, and read `Proto.Context` in the body. Each data row is its own context and its own trace, so `[DataRow]` rows convert together. Gate: converted tests get a trace.
- [ ] **4. Run `dotnet test`.** Gate: green.
- [ ] **5. Delete the per-class harness** the converted tests no longer need. Gate: no test shares a fixed row, tenant or file (see [Concurrency](../foundation/concurrency.md)).

Per-class pitfalls: there is no assembly-wide auto-wrap, so every converted method carries `[ProtoTest]`. Skips have no first-class MSTest property: the reason travels on the display name (`Method (skipped: {reason})`) and `LogOutput`. The attribute exposes no cancellation token, so a converted test starts from `CancellationToken.None`.

```csharp
// Before: per-class harness with its own client.
[TestClass]
public class OrderTests
{
    private readonly HttpClient _client = new() { BaseAddress = new Uri("http://localhost:5000") };

    [TestMethod]
    public async Task Orders_endpoint_responds()
    {
        using var response = await _client.GetAsync("/api/orders");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}

// After: the arrangement lives in the host, the test reads the context.
[TestClass]
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

The hooks, the per-row lifecycle and the skip path are on [MSTest](./mstest.md).

## TUnit: the order that stays green

The same five steps, with TUnit's executor instead of an attribute swap:

- [ ] **1. Add the package** (`ProtoTest.TUnit`) plus the integration packages the tests use. Gate: the solution builds.
- [ ] **2. Write the host once**: register `[assembly: TestExecutor<ProtoTestExecutor>()]` for the assembly, and initialize the host from a class deriving from `ProtoTestAssembly` with `[Before(Assembly)]` calling `InitializeAsync` and `[After(Assembly)]` calling `CleanupAsync`. Gate: plain tests still pass (wrapped).
- [ ] **3. Convert one class**: keep TUnit's `[Test]`. The executor wraps every test in the assembly, so there is no attribute to swap. Move the arrangement into the host and read `Proto.Context` in the body. Gate: converted tests get a trace.
- [ ] **4. Run `dotnet test`.** On .NET SDK 10 the project needs the Microsoft.Testing.Platform opt-in in `global.json` first. Run the command from that directory, or name the project from at or under it (see [TUnit](./tunit.md)). Gate: green.
- [ ] **5. Delete the per-class harness** the converted tests no longer need. Gate: no test shares a fixed row, tenant or file (see [Concurrency](../foundation/concurrency.md)).

Per-class pitfalls: the executor applies to every test in the assembly, so plain tests run wrapped rather than untouched. A source-generated test with no reflection `MethodInfo` runs unwrapped, with no context. The live `TestContext.CancellationToken` feeds the lifecycle, so the test and its hooks observe TUnit's per-test token.

```csharp
// Before: per-class harness with its own client.
[Application("Api")]
public class OrderTests
{
    private readonly HttpClient _client = new() { BaseAddress = new Uri("http://localhost:5000") };

    [Test]
    public async Task Orders_endpoint_responds()
    {
        using var response = await _client.GetAsync("/api/orders");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }
}

// After: the arrangement lives in the host, the test reads the context.
[Application("Api")]
public class OrderTests
{
    [Test]
    public async Task Orders_endpoint_responds()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

The executor, the hooks and the interception path are on [TUnit](./tunit.md).

## Limits

- A v2 class without the collection attribute fails that ProtoTest test and names the missing collection fixture. Convert the class in one change.
- The host is one per test process, started by the collection or assembly hook. A second registration does not layer a second host.
- Conversion does not fix shared state between tests. A class that writes to a fixed row, tenant or file still needs its own isolation, as [Concurrency](../foundation/concurrency.md) explains.
