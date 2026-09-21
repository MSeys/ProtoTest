# ProtoTest.Xunit

Connects the ProtoTest lifecycle to xUnit.net v2.

```bash
dotnet add package ProtoTest.Xunit
```

Register a `ProtoTestAssembly` as an xUnit collection fixture, add test classes to that collection and use `[ProtoTestFact]` or `[ProtoTestTheory]`.

Each theory row receives its own ProtoTest lifecycle. xUnit v2 has no native attachment API, so attachment paths are written to the test output.

## Learn more

- [xUnit v2 setup](https://prototest.dev/docs/runners/xunit)
- [Test runners](https://prototest.dev/docs/runners/overview)
- [Adapter tests](https://github.com/MSeys/ProtoTest/tree/main/tests/ProtoTest.Xunit.Tests)
