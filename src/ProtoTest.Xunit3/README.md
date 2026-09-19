# ProtoTest.Xunit3

Connects the ProtoTest lifecycle and native attachments to xUnit.net v3 through an assembly fixture.

```bash
dotnet add package ProtoTest.Xunit3
```

## Quick start

```csharp
[assembly: AssemblyFixture(typeof(TestSetup))]

public sealed class TestSetup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder.AddRest(rest => rest.AddClient("Api"));
    }
}

public sealed class OrderTests
{
    [ProtoTestFact]   // [ProtoTestTheory] for theories
    public async Task Scenario()
    {
        using var response = await Proto.Context.Rest().GetAsync("/orders/42");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## What it adds

- **Assembly host** — register the `ProtoTestAssembly` fixture with `[assembly: AssemblyFixture(typeof(...))]`; the message names that call when the fixture is missing.
- **Test attributes** — `[ProtoTestFact]` and `[ProtoTestTheory]` implement `IBeforeAfterTestAttribute` and delegate to the shared lifecycle handler.
- **Skip conditions** — `IProtoSkipCondition` attributes are evaluated before `StartTestAsync` and reported through `Assert.Skip`.
- **Outcomes** — `Passed`, `Skipped`/`NotRun` → skipped, `Failed` with a `ProtoTraceError`, and `Unknown` for unmapped states.
- **Attachments** — native attachments through `TestContext.Current.AddAttachment`.
- **Tracing** — Core's lifecycle entries with fully qualified test names via `ProtoTestName.FromMethod`.

The lifecycle handler is synchronous-over-async, so xUnit v3 is the supported runner for this adapter.

## Learn more

- [xUnit v3 adapter guide](https://prototest.dev/docs/runners/xunit3)
- [Adapter outcome tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Xunit3.Tests/OutcomeTests.cs)
