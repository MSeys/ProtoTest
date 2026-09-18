# ProtoTest.Xunit

Connects the ProtoTest lifecycle to xUnit.net v2.

```bash
dotnet add package ProtoTest.Xunit --prerelease
```

Create one collection fixture derived from `ProtoTestAssembly`, place participating test classes in that collection, and use `[ProtoTestFact]` or `[ProtoTestTheory]` in place of xUnit's `[Fact]`/`[Theory]` — no separate `[ProtoTest]` attribute is needed.

```csharp
[ProtoTestFact]
public async Task Scenario() { }
```

xUnit.net v2 has no native attachment API; materialized attachment paths are written to test output. See the [adapter guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/runners/xunit.md).
