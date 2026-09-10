# ProtoTest.Xunit

Connects the ProtoTest lifecycle to xUnit.net v2.

```bash
dotnet add package ProtoTest.Xunit --prerelease
```

Create one collection fixture derived from `ProtoTestAssembly`, place participating test classes in that collection, and combine xUnit's `[Fact]` or `[Theory]` with `[ProtoTest]`.

```csharp
[Fact, ProtoTest]
public async Task Scenario() { }
```

xUnit.net v2 has no native attachment API; materialized attachment paths are written to test output. See the [adapter guide](https://github.com/matthiasseys/ProtoTest/blob/main/docs/integrations/nunit.md).
