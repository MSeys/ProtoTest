---
sidebar_position: 3
title: xUnit v3
description: "Register ProtoTest with xUnit v3: the assembly fixture, [ProtoTestFact] and [ProtoTestTheory], what the adapter changes and its limits."
---

# xUnit v3

`ProtoTest.Xunit3` uses xUnit v3's assembly fixture and its `IBeforeAfterTestAttribute` hooks, so each test gets a ProtoTest context and artifacts go through xUnit's native attachment API.

## Install

```bash
dotnet add package ProtoTest.Xunit3
```

ProtoTest targets **.NET 8, 9 and 10**, and needs **xunit.v3 4.0.0 or newer**. The `dotnet new prototest` template defaults to `net10.0`; pass `--framework net8.0` or `--framework net9.0` for an older runtime.

**On .NET SDK 10, an xUnit v3 project runs on Microsoft.Testing.Platform.** The `dotnet new prototest` template carries the opt-in:

```json title="global.json"
{
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

With that file in the project or solution directory, run `dotnet test` from that directory. `dotnet test --project Starter.Tests/Starter.Tests.csproj` and `dotnet test Starter.slnx` work from that directory; from outside it they resolve through the old VSTest path and fail with `VSTest target is no longer supported` instead of running tests.

```bash
dotnet test                                            # right: run from the global.json directory
dotnet test --project Starter.Tests/Starter.Tests.csproj  # right: name the project, from the global.json directory
dotnet test Starter.slnx                              # right: name the solution, from the global.json directory
# from outside the global.json directory: cd there first; --project and the solution path fail through the old VSTest path
```

## Register

Register the setup class with `[assembly: AssemblyFixture(...)]`. No collection is needed, and it applies to every test in the assembly.

```csharp
using ProtoTest.Core;
using ProtoTest.Xunit3;
using Xunit;

[assembly: AssemblyFixture(typeof(Setup))]

public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder.AddApplication("Api", app => app.AddRest(rest => rest.AddClient("Api")));
}
```

Without the assembly fixture the host is not initialized. `ProtoTestAssembly.Host` throws an `InvalidOperationException` that names the missing registration.

The test attributes are `[ProtoTestFact]` and `[ProtoTestTheory]`, and both implement xUnit v3's `IBeforeAfterTestAttribute`, so their `Before` and `After` run around every test case.

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;
using ProtoTest.Xunit3;

namespace Orders.Tests;

[Application("Api")]
public class OrderTests
{
    [ProtoTestFact]
    public async Task Orders_endpoint_responds()
    {
        using var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

A `[ProtoTestTheory]` behaves the same way, and each `[InlineData]` row is a test of its own.

A plain `[Fact]` keeps running unchanged next to the converted tests; it simply has no `Proto.Context`, or add `[assembly: ProtoTestAutoWrap]` to give it one. [Bring an existing xUnit suite](./bring-your-existing-suite.md) covers the incremental path for both xUnit versions.

**Low-ceremony mode.** Add `[assembly: ProtoTestAutoWrap]` and every plain `[Fact]` and `[Theory]` runs through the same lifecycle. A test that carries `[ProtoTestFact]` or `[ProtoTestTheory]` keeps its own handler and is never wrapped twice.

## The context window

`|` is the host bar, `[]` is the context. Construction and disposal stay outside it:

```text
|[assembly fixture (host)]|  ctor + InitializeAsync OUTSIDE  [[before | body | after]]
```

Move context reads into `IAsyncLifetime.InitializeAsync` or the body. xUnit v2 is the mirror image: its constructor already sees the context.

## What the adapter changes

| Item | What the adapter does |
| --- | --- |
| The host | Starts once through the assembly fixture, which applies to every test in the assembly. |
| The test attributes | `[ProtoTestFact]` and `[ProtoTestTheory]` replace `[Fact]` and `[Theory]` and carry the lifecycle themselves. Both are `AllowMultiple = false`. |
| The lifecycle | `Before` resolves the attributes and conditions, then starts the context. `After` reads `TestContext.Current.TestState` and completes it with the mapped result. |
| The context window | The context starts after class construction and `IAsyncLifetime.InitializeAsync`, and completes before class disposal. Class-level setup and cleanup stay outside it, unlike xUnit v2, whose constructor already sees the context. |
| Scheduling | The lifecycle handler calls the host synchronously (`GetAwaiter().GetResult()`), so xUnit v3 needs a synchronizing context. |
| Cancellation | `TestContext.Current.CancellationToken` is available in the before-attribute, and the lifecycle starts the context with it. |
| Outcomes | `Passed` to `Passed`, `Skipped` and `NotRun` to `Skipped`, `Failed` to `Failed` with the exception type, message and stack. A failed `OperationCanceledException` or `TaskCanceledException` maps to `Cancelled`. Anything else is `Unknown`. |
| Skips | A skip condition calls `Assert.Skip(reason)` before the lifecycle starts, so no trace entry is written and the reason is reported as given. A body-level `Assert.Skip` is a state on a started context and maps to `Skipped`. |
| Attachments | `TestContext.Current.AddAttachment(name, bytes, replaceExistingValue: false, mediaType)`, so artifacts appear with the test in xUnit's output. |
| Test names | xUnit's display name, with the row's arguments included. |
| Auto-wrap | Optional. `[assembly: ProtoTestAutoWrap]` wraps every plain `[Fact]` and `[Theory]`, and steps aside for a method that carries a ProtoTest attribute. |

## Limits

- The lifecycle handler is synchronous-over-async, so xUnit v3 needs a synchronizing context.
- The attributes are `AllowMultiple = false`, like the `Fact` and `Theory` attributes they replace.
- The context starts in the before-attribute, so class construction, `IAsyncLifetime.InitializeAsync` and class disposal run outside it.
- **Leaves the trace open.** A sibling `IBeforeAfterTestAttribute` that throws prevents xUnit from running the after methods, so the context never completes and the test's trace stays open. Keep other before/after attributes from throwing.
- The assembly fixture is mandatory. There is no collection-level variant.
- Auto-wrap is assembly-wide: the attribute is declared on the assembly, and there is no per-class opt-in.
- `Unknown` is the fallback for an unmapped result state, so extend the mapping deliberately if a new xUnit state appears.

## Learn more

- [Test runners](./overview.md): the five adapters side by side.
- [Skip conditions](../foundation/skip-conditions.md): the conditions every adapter evaluates.
- [Lifecycle](../foundation/lifecycle.md): the hooks and attributes around a test.
