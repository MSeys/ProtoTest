# ProtoTest.TUnit

Connects the ProtoTest lifecycle and artifacts to TUnit.

```bash
dotnet add package ProtoTest.TUnit --prerelease
```

Register the executor once for the assembly, then initialize and clean up a `ProtoTestAssembly` from TUnit assembly hooks.

```csharp
[assembly: TestExecutor<ProtoTestExecutor>()]
```

Tests continue to use TUnit's `[Test]`; the executor surrounds each test with the shared ProtoTest lifecycle. See the [adapter guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/runners/tunit.md).
