# ProtoTest.Xunit3

Connects the ProtoTest lifecycle and native attachments to xUnit.net v3.

```bash
dotnet add package ProtoTest.Xunit3 --prerelease
```

Register one assembly fixture derived from `ProtoTestAssembly`, then use `[ProtoTestFact]` or `[ProtoTestTheory]`.

```csharp
[assembly: AssemblyFixture(typeof(TestSetup))]

[ProtoTestFact]
public async Task Scenario() { }
```

See the [adapter guide](https://github.com/matthiasseys/ProtoTest/blob/main/docs/integrations/nunit.md).
