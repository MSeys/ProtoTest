# ProtoTest.Data

ProtoTest.Data constructs deterministic test data while keeping scenario-relevant values visible in Arrange code.

```csharp
builder.AddData(data =>
{
    data.AddDefaultsFromAssembly(typeof(TestAssembly).Assembly);
});
```

```csharp
var invoice = Proto.Context.Data().For<Invoice>()
    .With(x => x.Total, 125m)
    .With(x => x.Status, InvoiceStatus.Overdue)
    .Build();
```

Defaults belong in feature-local modules:

```csharp
public sealed class InvoiceDataDefaults : IProtoDataDefaultsModule
{
    public void Configure(ProtoDataConfiguration data)
    {
        data.Values.Use<InvoiceId>(context => InvoiceId.From(context.NextGuid()));

        data.For<Invoice>()
            .Default(x => x.Currency, Currency.EUR);
    }
}
```

Explicit values win over member defaults, which win over type providers and safe built-in values. Missing semantic values cause a `ProtoDataException` rather than being guessed.

DDD types can retain their invariants by registering a domain factory:

```csharp
data.For<Invoice>()
    .ConstructUsing(context => Invoice.Create(
        context.Value<InvoiceId>(nameof(Invoice.Id)),
        context.Value<Money>(nameof(Invoice.Total))));
```

Multiple `AddData(...)` calls compose on the same host. Numeric, enum, date and other potentially semantic values are never guessed; register a default or keep them explicit in the test.

Sensitive trace values can be redacted per member or value type:

```csharp
data.For<User>().Redact(x => x.AccessToken);
data.RedactValueType<Password>();
```

Cross-cutting conventions can extend the value pipeline through `IProtoDataValueResolver`. They run after exact member and type providers and before safe built-in generation.

Every `Build()` and `Explain()` operation is recorded in ProtoTrace. Each resolved member is a child trace event containing its value source.

## Provisioning

Register one application-specific route for a data type:

```csharp
builder
    .AddData()
    .AddDataProvisioner<Invoice, InvoiceProvisioner>();
```

The test remains focused on relevant state:

```csharp
var invoice = await Proto.Context.Data().For<Invoice>()
    .With(x => x.Total, Money.EUR(125m))
    .CreateAsync();
```

Bulk construction keeps fixed scenario values while resolving a fresh deterministic sequence for every item:

```csharp
var users = Proto.Context.Data().For<User>()
    .With(x => x.Role, Roles.Member)
    .BuildMany(7);
```

An `IProtoDataProvisioner<T>` may use commands, events, an API, a repository, or direct persistence. Its optional `Cleanup` is disposed in reverse creation order when the test context is disposed. `data.create`, `data.provision`, and `data.cleanup` operations are written to ProtoTrace automatically.

Provisioned values are tracked as `value:{type}:{identity}`, with the result CLR type in snake_case — `InvoiceLine` becomes `invoice_line` — so an application identity attribute with the same prefix (`invoice_line.number`) correlates with the test-side value.
