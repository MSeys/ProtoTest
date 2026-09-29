---
sidebar_position: 7
title: Bring an existing xUnit suite
description: "Move an existing xUnit v2 or v3 suite onto ProtoTest test by test: what keeps running, what converts, and the order that stays green."
---

import TabbedCode from '@site/src/components/TabbedCode';

# Bring an existing xUnit suite

Convert one class at a time. Plain tests keep running.

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
A converted v2 class without `[Collection(ProtoTestCollection.Name)]` that reaches `Proto.Context` crashes the test host and aborts the whole run. Add it in the same change.
:::

:::warning[Step 4 on SDK 10 with v3: the MTP opt-in comes first]
An xUnit v3 project on .NET SDK 10 needs the Microsoft.Testing.Platform opt-in in `global.json` before `dotnet test` runs it. [xUnit v3](./xunit3.md) shows the file and the working commands.
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
| A converted class | `[Collection(ProtoTestCollection.Name)]` is mandatory; without it the test-host crash aborts the run | nothing extra; the assembly fixture covers every class |
| The constructor | Already runs inside the context, so `Proto.Context` works there | Runs before the context; move context reads into `IAsyncLifetime.InitializeAsync` or the test body |
| Attachments | Files land under `%TEMP%\ProtoTest\attachments`, the path prints to the console with `--logger "console;verbosity=detailed"` | `TestContext.Current.AddAttachment(...)`, so artifacts appear with the test in xUnit's output |

The registration details, outcome mapping and limits are on [xUnit v2](./xunit.md) and [xUnit v3](./xunit3.md).

## Limits

- A v2 class without the collection attribute that reaches `Proto.Context` crashes the test host and aborts the whole run, so convert the class in one change.
- The host is one per test process, started by the collection or assembly hook. A second registration does not layer a second host.
- Conversion does not fix shared state between tests. A class that writes to a fixed row, tenant or file still needs its own isolation; see [Concurrency](../foundation/concurrency.md).
