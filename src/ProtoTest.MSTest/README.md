# ProtoTest.MSTest

Connects the ProtoTest lifecycle and attachments to MSTest through assembly initialize/cleanup and `[ProtoTest]`.

```bash
dotnet add package ProtoTest.MSTest
```

## Quick start

```csharp
[TestClass]
public sealed class TestSetup : ProtoTestAssembly
{
    [AssemblyInitialize]
    public static Task Initialize(TestContext context)
        => InitializeAsync(builder => builder.AddRest(rest => rest.AddClient("Api")));

    [AssemblyCleanup]
    public static Task Cleanup() => CleanupAsync();
}

public sealed class OrderTests
{
    [ProtoTest]   // replaces [TestMethod]
    public async Task Scenario()
    {
        using var response = await Proto.Context.Rest().GetAsync("/orders/42");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## What it adds

- **Assembly host** — derive from `ProtoTestAssembly` and call `InitializeAsync(...)` from `[AssemblyInitialize]` and `CleanupAsync()` from `[AssemblyCleanup]`; `Host` throws before initialization and double initialization throws.
- **Test attribute** — `[ProtoTest]` (`TestMethodAttribute`) starts one lifecycle around all data rows and completes it in `finally`.
- **Skip conditions** — `IProtoSkipCondition` attributes are evaluated before `StartTestAsync`; the result is `Ignored` with the reason in the display name and `LogOutput`.
- **Outcomes** — all passed → `Passed`; all ignored/inconclusive → `Skipped`; a mix → `Partial`; the first failed/error/timeout/aborted row → `Failed`.
- **Attachments** — materialized attachments are appended to the first data row's `TestResult.ResultFiles`.
- **Tracing** — Core's lifecycle entries with fully qualified test names.

MSTest has no public dynamic-skip API, so a skip reason travels on the display name and `LogOutput`, and attachments land on the first result rather than each data row.

## Learn more

- [MSTest adapter guide](https://prototest.dev/docs/runners/mstest)
- [Adapter outcome tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.MSTest.Tests/OutcomeTests.cs)
