# ProtoTest.MSTest

Connects the ProtoTest lifecycle and attachments to MSTest.

```bash
dotnet add package ProtoTest.MSTest --prerelease
```

Derive an assembly setup class from `ProtoTestAssembly`, call `InitializeAsync` from `[AssemblyInitialize]`, and call `CleanupAsync` from `[AssemblyCleanup]`. Mark tests with `[ProtoTest]`.

```csharp
[AssemblyCleanup]
public static Task Cleanup() => CleanupAsync();
```

See the [adapter guide](https://github.com/matthiasseys/ProtoTest/blob/main/docs/integrations/nunit.md).
