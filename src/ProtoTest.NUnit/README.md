# ProtoTest.NUnit

Connects the ProtoTest lifecycle to NUnit.

```bash
dotnet add package ProtoTest.NUnit
```

Create a `[SetUpFixture]` that inherits `ProtoTestAssembly` and compose the suite in `Configure`, then use `[ProtoTest]` instead of `[Test]`:

```csharp
[SetUpFixture]
public class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) { }
}

[ProtoTest]
public async Task CheckoutTotalsMatch()
{
    // Proto.Context is ready here.
}
```

To skip that, add `[assembly: ProtoTestAutoWrap]` and every plain `[Test]` runs through the same lifecycle.

The adapter starts and completes the test context, evaluates ProtoTest skip conditions and publishes attachments through NUnit.

## Learn more

- [NUnit setup](https://prototest.dev/docs/runners/nunit)
- [Test runners](https://prototest.dev/docs/runners/overview)
- [Adapter tests](https://github.com/MSeys/ProtoTest/tree/main/tests/ProtoTest.NUnit.Tests)
