# ProtoTest.MSTest

Connects the ProtoTest lifecycle to MSTest.

```bash
dotnet add package ProtoTest.MSTest
```

Initialize a `ProtoTestAssembly` from `[AssemblyInitialize]`, clean it up from `[AssemblyCleanup]` and use `[ProtoTest]` instead of `[TestMethod]`.

The adapter handles the test context, skip conditions, outcomes and MSTest result attachments. One ProtoTest lifecycle wraps all data rows of a test method.

## Learn more

- [MSTest setup](https://prototest.dev/docs/runners/mstest)
- [Test runners](https://prototest.dev/docs/runners/overview)
- [Adapter tests](https://github.com/MSeys/ProtoTest/tree/main/tests/ProtoTest.MSTest.Tests)
