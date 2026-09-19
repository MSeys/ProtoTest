---
sidebar_position: 3
title: xUnit v3
description: "Set up ProtoTest with xUnit v3: the assembly fixture, [ProtoTestFact] tests, outcomes and native attachments."
---

# xUnit v3

```bash
dotnet add package ProtoTest.Xunit3
```

xUnit v3 is the best-supported runner: it reports real test outcomes and has native attachments.

## Assembly setup

Register the setup class with `[assembly: AssemblyFixture(...)]` — no collection needed, and it applies to every test in the assembly.

```csharp
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit3;
using Xunit;

[assembly: AssemblyFixture(typeof(Setup))]

public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api")));
}
```

## Writing a test

`[ProtoTestFact]` and `[ProtoTestTheory]` derive from `FactAttribute` and `TheoryAttribute`, so they replace them outright — don't add `[Fact]` as well.

```csharp
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit3;
using System.Net;
using Xunit;

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

**Outcomes are recorded properly.** The lifecycle handler reads `TestContext.Current.TestState` and maps Passed / Skipped / NotRun / Failed onto the ProtoTest result, including the exception type, message and stack trace — so a failed test shows up as failed in the trace and reports.

**Attachments are native.** Artifacts go through `TestContext.Current.AddAttachment(name, bytes, mediaType)` and appear in the runner's own output.
