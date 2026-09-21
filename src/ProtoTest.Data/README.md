# ProtoTest.Data

Test-data builders with reusable defaults, provisioners and per-test references.

```bash
dotnet add package ProtoTest.Data
```

```csharp
var invoice = Proto.Context.Data().For<Invoice>()
    .With(x => x.Total, 125m)
    .Build();
```

Defaults can provide common values, while a provisioner can create data through the application and clean it up during teardown. ProtoTest does not guess unresolved domain values.

## Learn more

- [Data integration](https://prototest.dev/docs/integrations/data/)
- [Defaults and generated values](https://prototest.dev/docs/integrations/data/defaults)
- [Provisioners](https://prototest.dev/docs/integrations/data/provisioners)
