# ProtoTest.TUnit

Connects the ProtoTest lifecycle to TUnit through `ProtoTestExecutor`.

```bash
dotnet add package ProtoTest.TUnit
```

Register `[assembly: TestExecutor<ProtoTestExecutor>()]` and initialize a `ProtoTestAssembly` from TUnit's assembly hooks. Tests continue to use TUnit's own `[Test]` attribute.

```csharp
[assembly: TestExecutor<ProtoTestExecutor>()]

public class Setup : ProtoTestAssembly
{
    [Before(Assembly)]
    public static Task AssemblyInitAsync(AssemblyHookContext _)
        => InitializeAsync(builder => { });

    [After(Assembly)]
    public static Task AssemblyCleanupAsync(AssemblyHookContext _)
        => CleanupAsync();
}
```

The adapter handles the test context, skip conditions, outcomes and TUnit artifacts.

## Learn more

- [TUnit setup](https://prototest.dev/docs/runners/tunit)
- [Test runners](https://prototest.dev/docs/runners/overview)
- [Adapter tests](https://github.com/MSeys/ProtoTest/tree/main/tests/ProtoTest.TUnit.Tests)
