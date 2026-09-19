# ProtoTest.Xunit

Connects the ProtoTest lifecycle to xUnit.net v2 through a collection fixture and `[ProtoTestFact]`/`[ProtoTestTheory]`.

```bash
dotnet add package ProtoTest.Xunit
```

## Quick start

```csharp
public sealed class TestSetup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder.AddRest(rest => rest.AddClient("Api"));
    }
}

[CollectionDefinition("ProtoTest")]
public sealed class ProtoTestCollection : ICollectionFixture<TestSetup>;

[Collection("ProtoTest")]
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

- **Assembly host** — one collection fixture derived from `ProtoTestAssembly` starts and disposes the host for the collection; test classes must join that collection.
- **Test attributes** — `[ProtoTestFact]` and `[ProtoTestTheory]` replace xUnit's `[Fact]`/`[Theory]`; each theory row is its own test with its own lifecycle.
- **Skip conditions** — `[RequiresCapability(...)]`, `[RequiresInProcess]` and other `IProtoSkipCondition` attributes are evaluated before invocation and become xUnit's `SkipReason`; no lifecycle starts.
- **Outcomes** — `Passed`, `Failed`, `Cancelled`; skipped tests produce no trace entry.
- **Attachments** — xUnit v2 has no native attachment API, so materialized attachment paths are written to test output.
- **Tracing** — Core's `test.setup`/`test.execution`/`test.teardown` entries, one test per theory row.

The skip decision is fixed before invocation (xUnit v2 has no dynamic skip), and the collection fixture must be wired or the host is never initialized.

## Learn more

- [xUnit v2 adapter guide](https://prototest.dev/docs/runners/xunit)
- [Adapter outcome tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Xunit.Tests/ProtoTestFactOutcomeTests.cs)
