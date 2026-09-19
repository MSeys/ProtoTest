# ProtoTest.Data

Deterministic test data with member defaults, provisioners and an identity map — without ever guessing a semantic value.

```bash
dotnet add package ProtoTest.Data
```

```csharp
builder.AddData(data => data.AddDefaultsFromAssembly(typeof(NorthstarDataDefaults).Assembly));
```

## Quick start

```csharp
// Explicit values win over defaults, which win over providers and safe built-ins.
var invoice = Proto.Context.Data().For<Invoice>()
    .With(x => x.Total, 125m)
    .With(x => x.Status, InvoiceStatus.Overdue)
    .Build();

// A provisioner creates through the application; the result enters the identity map.
builder
    .AddData()
    .AddDataProvisioner<Invoice, InvoiceProvisioner>();

var created = await Proto.Context.Data().For<Invoice>()
    .With(x => x.Total, Money.EUR(125m))
    .CreateAsync();

var again = Proto.Context.Data().Ref<Invoice>(created.Number);
```

## What it adds

- **Construction** — `Proto.Context.Data()` exposes `For<T>()` (a `ProtoDataObjectBuilder<T>`) and `Ref<T>(identity?)`; build with `With`, `Build`, `CreateAsync`, `BuildMany` or `CreateManyAsync`.
- **Precedence per member** — explicit `With`, member default, exact type provider, registered `IProtoDataValueResolver`s, safe built-ins, then the constructor default; anything unresolved throws a `ProtoDataException` naming the member.
- **Configuration** — `AddData(data => …)`: `Values.Use<T>(...)`, `For<T>().Default(...)`, `ConstructUsing(...)`, `Redact(...)`, `RedactValueType<T>()`, `AddDefaults<TModule>()` and `AddDefaultsFromAssembly(assembly)`.
- **Provisioners** — `AddDataProvisioner<T, TProvisioner>()` (or the input/result overload); an optional `Cleanup` is released in reverse creation order during teardown.
- **Identity map** — per-test `Ref<T>(identity)` matches `CreateAsync` results case-sensitively; `data.provision` records the identity and a `value:{type}:{identity}` tracked item.
- **Tracing** — `data.build`/`data.explain`/`data.create`/`data.build_many`/`data.create_many`, `data.provision`, `data.cleanup` and per-member `data.value.resolve` events with their source.

Values are never invented, `Build()` results never enter the identity map, and redaction protects only the ProtoTrace graph.

## Learn more

- [Data guide](https://prototest.dev/docs/integrations/data/)
- [NorthstarData.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.SampleApp.Testing/NorthstarData.cs)
