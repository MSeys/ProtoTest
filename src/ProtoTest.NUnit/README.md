# ProtoTest.NUnit

Connects the ProtoTest lifecycle to NUnit, evaluates skip conditions before setup, and publishes attachments through NUnit results.

```bash
dotnet add package ProtoTest.NUnit
```

## Quick start

```csharp
[SetUpFixture]
public sealed class TestSetup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        builder.AddRest(rest => rest.AddClient("Api"));
    }
}

public sealed class OrderTests
{
    [ProtoTest]   // replaces [Test]
    public async Task Scenario()
    {
        using var response = await Proto.Context.Rest().GetAsync("/orders/42");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## What it adds

- **Assembly host** — derive from `ProtoTestAssembly` under `[SetUpFixture]` and register integrations in `Configure(IProtoHostBuilder)`; `ProtoTestAssembly.Host` exposes the running host.
- **Test attribute** — `[ProtoTest]` (`TestAttribute`, `ITestAction`) starts the lifecycle in `BeforeTest` and completes it in `AfterTest`.
- **Skip conditions** — `[RequiresCapability(...)]`, `[RequiresInProcess]` and other `IProtoSkipCondition` attributes are evaluated before `StartTestAsync` and reported through `Assert.Ignore`.
- **Outcomes** — `Passed`, `Failed` (`NUnit.{Label}`), `Skipped`, `Inconclusive` → skipped and `Warning` → `Partial`; a skipped test starts no lifecycle and produces no trace entry.
- **Attachments** — materialized attachments are published with `TestContext.AddTestAttachment`.
- **Tracing** — Core's `test.setup`/`test.execution`/`test.teardown` entries, with fully qualified test names.

`ITestAction` is synchronous, so the adapter blocks while the async lifecycle runs; skip conditions are evaluated in attribute-resolution order (class before method), not `Order`-sorted.

## Learn more

- [NUnit adapter guide](https://prototest.dev/docs/runners/nunit)
- [DeliveryJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/DeliveryJourney.cs)
