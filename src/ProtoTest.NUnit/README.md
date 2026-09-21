# ProtoTest.NUnit

Connects the ProtoTest lifecycle to NUnit.

```bash
dotnet add package ProtoTest.NUnit
```

Create a `[SetUpFixture]` that inherits `ProtoTestAssembly`, then use `[ProtoTest]` instead of `[Test]` on ProtoTest scenarios.

The adapter starts and completes the test context, evaluates ProtoTest skip conditions and publishes attachments through NUnit.

## Learn more

- [NUnit setup](https://prototest.dev/docs/runners/nunit)
- [Test runners](https://prototest.dev/docs/runners/overview)
- [Demo tests](https://github.com/MSeys/ProtoTest/tree/main/samples/ProtoTest.Demo)
