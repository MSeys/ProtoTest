# ProtoTest.TUnit

Connects the ProtoTest lifecycle and artifacts to TUnit through an `ITestExecutor`; tests keep TUnit's own `[Test]`.

```bash
dotnet add package ProtoTest.TUnit
```

## Quick start

```csharp
[assembly: TestExecutor<ProtoTestExecutor>()]

public sealed class TestSetup : ProtoTestAssembly
{
    [Before(Assembly)]
    public static Task Initialize(AssemblyHookContext _)
        => InitializeAsync(builder => builder.AddRest(rest => rest.AddClient("Api")));

    [After(Assembly)]
    public static Task Cleanup(AssemblyHookContext _) => CleanupAsync();
}

public sealed class OrderTests
{
    [Test]
    public async Task Scenario()
    {
        using var response = await Proto.Context.Rest().GetAsync("/orders/42");
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

## What it adds

- **Executor** — `ProtoTestExecutor` implements `ITestExecutor`, wraps each test in the shared lifecycle and rethrows failures so TUnit still sees them.
- **Assembly host** — `ProtoTestAssembly` initialized from `[Before(Assembly)]` and cleaned up from `[After(Assembly)]`.
- **Skip conditions** — `IProtoSkipCondition` attributes are evaluated before `StartTestAsync` and reported through `TUnit.Core.Skip.Test`; a body-level `SkipTestException` also maps to skipped.
- **Outcomes** — `Passed`, `Skipped`, `Cancelled` (cancellation) and `Failed`; a teardown failure never replaces the original failure.
- **Attachments** — published per test through `context.Output.AttachArtifact`, so every data row gets its own artifacts.
- **Tracing** — Core's lifecycle entries with fully qualified test names.

There is no ProtoTest attribute: test discovery is entirely TUnit's, and narrower `[TestExecutor<T>]` scoping is not covered by the repository's tests.

## Learn more

- [TUnit adapter guide](https://prototest.dev/docs/runners/tunit)
- [Adapter outcome tests](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.TUnit.Tests/OutcomeTests.cs)
