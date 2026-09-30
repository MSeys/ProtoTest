# ProtoTest.MSTest

Connects the ProtoTest lifecycle to MSTest.

```bash
dotnet add package ProtoTest.MSTest
```

Initialize a `ProtoTestAssembly` from `[AssemblyInitialize]`, clean it up from `[AssemblyCleanup]` and use `[ProtoTest]` instead of `[TestMethod]`:

```csharp
[TestClass]
public class Setup : ProtoTestAssembly
{
    [AssemblyInitialize]
    public static Task AssemblyInitAsync(TestContext context)
        => InitializeAsync(builder => { });

    [AssemblyCleanup]
    public static Task AssemblyCleanupAsync() => CleanupAsync();
}
```

The adapter handles the test context, skip conditions, outcomes and MSTest result attachments. Each data row of a test method runs in its own ProtoTest lifecycle, like xUnit theory rows.

## Learn more

- [MSTest setup](https://prototest.dev/docs/runners/mstest)
- [Test runners](https://prototest.dev/docs/runners/overview)
- [Adapter tests](https://github.com/MSeys/ProtoTest/tree/main/tests/ProtoTest.MSTest.Tests)
