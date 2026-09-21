# ProtoTest.Xunit3

Connects the ProtoTest lifecycle to xUnit.net v3.

```bash
dotnet add package ProtoTest.Xunit3
```

Register a `ProtoTestAssembly` with `[assembly: AssemblyFixture(typeof(...))]` and use `[ProtoTestFact]` or `[ProtoTestTheory]`.

The adapter handles the test context, skip conditions, outcomes and native xUnit v3 attachments.

## Learn more

- [xUnit v3 setup](https://prototest.dev/docs/runners/xunit3)
- [Test runners](https://prototest.dev/docs/runners/overview)
- [Adapter tests](https://github.com/MSeys/ProtoTest/tree/main/tests/ProtoTest.Xunit3.Tests)
