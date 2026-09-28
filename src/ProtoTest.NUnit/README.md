# ProtoTest.NUnit

Connects the ProtoTest lifecycle to NUnit.

```bash
dotnet add package ProtoTest.NUnit
```

Create a `[SetUpFixture]` that inherits `ProtoTestAssembly`, then use `[ProtoTest]` instead of `[Test]` on ProtoTest scenarios. To skip that, add `[assembly: ProtoTestAutoWrap]` and every plain `[Test]` runs through the same lifecycle.

The adapter starts and completes the test context, evaluates ProtoTest skip conditions and publishes attachments through NUnit.

## Learn more

- [NUnit setup](https://prototest.dev/docs/runners/nunit)
- [Test runners](https://prototest.dev/docs/runners/overview)
- [Test suite](https://github.com/MSeys/ProtoTest/tree/main/samples/Northstar.ProtoTest)
