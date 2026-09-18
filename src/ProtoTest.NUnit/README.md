# ProtoTest.NUnit

Connects the ProtoTest lifecycle to NUnit and publishes ProtoTest attachments through NUnit test results.

```bash
dotnet add package ProtoTest.NUnit --prerelease
```

```csharp
[SetUpFixture]
public sealed class TestSetup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder)
    {
        // Register integrations and hooks.
    }
}

[ProtoTest]
public async Task Scenario() { }
```

See the [adapter guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/runners/nunit.md).
