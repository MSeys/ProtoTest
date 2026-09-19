---
sidebar_position: 2
title: xUnit v2
description: "Set up ProtoTest with xUnit v2: the collection fixture, [ProtoTest] tests, theories and outcomes."
---

# xUnit v2

```bash
dotnet add package ProtoTest.Xunit
```

## Assembly setup

`ProtoTestAssembly` implements xUnit's `IAsyncLifetime`, so it works as a collection fixture. Every test class that needs ProtoTest joins that collection.

```csharp
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit;
using Xunit;

public class ProtoTestFixture : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api")));
}

[CollectionDefinition(Name)]
public class ProtoTestCollection : ICollectionFixture<ProtoTestFixture>
{
    public const string Name = "ProtoTest Collection";
}
```

## Writing a test

Use `[ProtoTestFact]` and `[ProtoTestTheory]` in place of `[Fact]` and `[Theory]` — the same attribute names as the [xUnit v3](./xunit3.md) package.

```csharp
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit;
using System.Net;
using Xunit;

[Collection(ProtoTestCollection.Name)]
[Application("Api")]
public class OrderTests
{
    [ProtoTestFact]
    public async Task Orders_endpoint_responds()
    {
        var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.ShouldHaveHttpStatus(HttpStatusCode.OK);
    }

    [ProtoTestTheory]
    [InlineData("open")]
    [InlineData("paid")]
    public async Task Invoices_filter_by_state(string state)
    {
        var response = await Proto.Context.Rest()
            .GetAsync("/api/billing/invoices", new { state });

        response.ShouldHaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## Things to know

**Outcomes are recorded.** The ProtoTest context wraps the test invocation, and when the method returns ProtoTest reads xUnit's own failure record — so a failing test shows up as failed in the trace and reports, with its exception. Cancellation is recorded as cancelled.

**Each theory row is its own test.** Rows are recorded under xUnit's display name, arguments included — `….OrderTests.Invoices_filter_by_state(state: "open")` — so the trace keeps them apart.

**Skipped tests aren't recorded.** xUnit never invokes a skipped test, so no context is started for it.

**The lifecycle is fully async** — nothing blocks on a task.

**Attachments are written to disk, not attached.** xUnit v2 has no attachment API, so each artifact is materialised to a file and its path written to the console:

```
ProtoTest attachment 'rest-01-response': /path/to/TestResults/.../rest-01-response.json
```

**Forgetting `[Collection]` breaks the host.** Without it the fixture never runs, and the first test fails with `InvalidOperationException: ProtoHost is not initialized.`

## The older `[Fact]` + `[ProtoTest]` style

```csharp
[Fact]
[ProtoTest]
public async Task Orders_endpoint_responds() { … }
```

This still works, but `[ProtoTest]` is a `BeforeAfterTestAttribute`, and xUnit v2 only reports a test's result *after* those attributes have finished. So with this style every test is recorded with outcome `Unknown`, and the lifecycle blocks on async work (`.GetAwaiter().GetResult()`) because the hook is synchronous. `ProtoTestAttribute` is marked `[Obsolete]`; prefer `[ProtoTestFact]` / `[ProtoTestTheory]`.
