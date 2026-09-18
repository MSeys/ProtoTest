---
sidebar_position: 1
title: Overview
---

# Data

`ProtoTest.Data` builds test objects with deterministic defaults, so the only values you write in a test are the ones the test is *about* — and it can hand those objects to your application to create them for real.

```bash
dotnet add package ProtoTest.Data
```

## The idea

```csharp
var invoice = Proto.Context.Data().For<Invoice>()
    .With(x => x.Total, 125m)
    .With(x => x.Status, InvoiceStatus.Overdue)
    .Build();
```

A reader sees immediately that this test cares about an overdue invoice of 125. Every other property — the id, the customer, the currency — comes from [defaults you configure once](./defaults.md).

Two principles shape the library:

- **Deterministic.** Generated values are derived from the test id and position, so a re-run of the same test produces the same data.
- **No guessing.** Numbers, enums, dates and other values that could carry business meaning are never invented. If nothing provides one, you get a `ProtoDataException` telling you which member is missing — not a silently wrong `0`.

## Registering

```csharp
builder.AddData(data => data.AddDefaults<InvoiceDataDefaults>());
```

```csharp
public static IProtoHostBuilder AddData(this IProtoHostBuilder builder, Action<ProtoDataConfiguration>? configure = null);
```

Calling `AddData` more than once composes into the same configuration, so each feature area can register its own defaults. To pick up every module in an assembly:

```csharp
builder.AddData(data => data.AddDefaultsFromAssembly(typeof(Setup).Assembly));
```

## Building objects

`Proto.Context.Data()` returns an `IProtoData`; `For<T>()` returns a builder.

```csharp
ProtoDataObjectBuilder<T> With<TMember>(Expression<Func<T, TMember>> member, TMember value);

T Build();
IReadOnlyList<T> BuildMany(int count, Action<ProtoDataObjectBuilder<T>, int>? configure = null);

ValueTask<T> CreateAsync(CancellationToken cancellationToken = default);
ValueTask<TResult> CreateAsync<TResult>(CancellationToken cancellationToken = default);
ValueTask<IReadOnlyList<T>> CreateManyAsync(int count, Action<ProtoDataObjectBuilder<T>, int>? configure = null, CancellationToken cancellationToken = default);
ValueTask<IReadOnlyList<TResult>> CreateManyAsync<TResult>(int count, Action<ProtoDataObjectBuilder<T>, int>? configure = null, CancellationToken cancellationToken = default);

ProtoDataExplanation Explain();
```

**`Build`** only constructs the object in memory. **`CreateAsync`** builds it and passes it to a [provisioner](./provisioners.md) that creates it in your application — through an API, a repository, a database, whatever you choose.

### Several at once

The `configure` callback receives each item's builder and its zero-based index:

```csharp
var lines = Proto.Context.Data().For<OrderLine>()
    .BuildMany(3, (line, index) => line.With(x => x.Quantity, index + 1));
```

```csharp
var members = await Proto.Context.Data()
    .For<CreateUserRequest>()
    .With(request => request.Role, SampleRoles.Member)
    .CreateManyAsync<UserResponse>(7);
```

Values set with `With` before `BuildMany` apply to every item.

### Constructors

Objects are created through their single public constructor (matching parameters by name) or their parameterless one, and remaining writable properties are filled in. Records work naturally. Ambiguous constructors, required properties that can't be written, or `With` on a member that can't be set all throw `ProtoDataException` with an explanation.

For domain types that protect their invariants, register a [factory](./defaults.md#domain-factories) instead.

## Explaining a build

When a value surprises you, ask where it came from:

```csharp
var plan = Proto.Context.Data().For<Invoice>()
    .With(x => x.Total, 125m)
    .Explain();

foreach (var value in plan.Values)
    Console.WriteLine($"{value.MemberName} = {value.Value}  [{value.SourceKind}]");
```

```
Id = 4b1f0c…                       [TypeProvider]
Total = 125                        [Explicit]
Currency = EUR                     [MemberDefault]
Reference = Invoice.Reference-0001-00   [BuiltIn]
```

Each entry also carries `ValueType` and a `Source` — for defaults, the name of the module that registered them.

`SourceKind` is one of `Explicit`, `MemberDefault`, `TypeProvider`, `CustomResolver`, `BuiltIn` or `ConstructorDefault`. For types built through reflection, the explanation resolves the same plan the next `Build()` will construct. For a type registered with a [factory](./defaults.md#domain-factories), `Explain()` lists only the explicit `With(...)` values and the construction source, because the factory resolves its remaining inputs when `Build()` runs.

You rarely need to call it yourself: every `Build` and `Explain` is recorded in [ProtoTrace](../../advanced/prototrace.md), with each resolved member and its source as a child entry.

## Next

- [Defaults](./defaults.md) — value providers, member defaults, factories, resolvers and redaction.
- [Provisioners](./provisioners.md) — creating data in your application and cleaning it up.
