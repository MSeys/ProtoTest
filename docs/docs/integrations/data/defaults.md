---
sidebar_position: 2
title: Defaults
description: "Keep test-data defaults in small modules, one per feature area, next to the tests that use them."
---

# Defaults

Defaults live in **modules** — small classes, ideally one per feature area, next to the tests that use them.

```csharp
public sealed class InvoiceDataDefaults : IProtoDataDefaultsModule
{
    public void Configure(ProtoDataConfiguration data)
    {
        data.Values.Use<InvoiceId>(context => new InvoiceId(context.NextGuid()));

        data.For<Invoice>()
            .Default(x => x.Currency, "EUR")
            .Default(x => x.Customer, context => $"customer-{context.TestId}");
    }
}
```

```csharp
builder.AddData(data => data.AddDefaults<InvoiceDataDefaults>());
```

You can also configure inline in `AddData(data => …)`; the trace then reports the source as `Host configuration`.

## Where a value comes from

For each member, the first of these that applies wins:

1. **`With(...)`** in the test.
2. A **member default** — `data.For<T>().Default(x => x.Member, …)`.
3. A **type provider** — `data.Values.Use<TValue>(…)`, used for every member of that type.
4. A **custom resolver** — `IProtoDataValueResolver`, in registration order.
5. A **safe built-in**: `null` for nullable members, a generated string for `string`, a generated `Guid`, or an empty array / list / enumerable.
6. The constructor parameter's **default value**, if it has one.

If none apply, `ProtoDataException` names the member. Numbers, enums, dates, booleans and your own value objects are deliberately *not* on the built-in list — provide them explicitly or through a default.

Registering the same member, type or factory twice throws `ProtoDataException` with an "ambiguous" message. There's no last-one-wins, so two modules can't silently fight over a value.

## Member defaults

```csharp
data.For<CreateUserRequest>()
    .Default(request => request.Email,
        context => $"user-{context.TestId}-{context.ObjectSequence:D4}@example.test");
```

```csharp
ProtoDataTypeConfiguration<T> Default<TMember>(Expression<Func<T, TMember>> member, TMember value);
ProtoDataTypeConfiguration<T> Default<TMember>(Expression<Func<T, TMember>> member, Func<ProtoDataValueContext, TMember> provider);
```

Use the provider overload whenever the value must be unique — a constant email address breaks the moment two tests run in parallel.

## Type providers

A type provider supplies a value for **every** member of a type, on any object:

```csharp
data.Values.Use<Money>(_ => new Money(10m, "EUR"));
data.Values.Use<TenantId>(context => new TenantId(context.NextGuid()));
```

## The value context

Both kinds of provider receive a `ProtoDataValueContext`:

| Member | |
| --- | --- |
| `TestId` | the running test's id |
| `ObjectSequence` | the object's position among objects built in this test |
| `TargetType`, `ValueType`, `MemberName` | what's being resolved |
| `Services` | the test's service provider |
| `NextGuid()` | a deterministic GUID |
| `NextString()` | a deterministic string like `Invoice.Reference-0001-00` |

`NextGuid()` is derived from the test id, object sequence, type, member and a counter — the same test produces the same GUIDs on every run, while different tests never collide.

## Domain factories

When a type enforces invariants through a factory method, let ProtoTest call it instead of a constructor:

```csharp
data.For<Invoice>()
    .ConstructUsing(context => Invoice.Create(
        context.Value<InvoiceId>(nameof(Invoice.Id)),
        context.Value<Money>(nameof(Invoice.Total))));
```

`context.Value<TValue>(memberName)` resolves that member through the normal pipeline — so `With(x => x.Total, …)` in a test still flows into the factory. `ProtoDataConstructionContext` also exposes `Services`.

## Custom resolvers

For conventions that span many types — "every property called `CreatedAt` is a fixed clock value", say:

```csharp
public sealed class FixedClockResolver : IProtoDataValueResolver
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);

    public bool TryResolve(ProtoDataValueContext context, out ProtoDataResolvedValue value)
    {
        if (context.ValueType == typeof(DateTimeOffset) && context.MemberName.EndsWith("At"))
        {
            value = new ProtoDataResolvedValue(Now, "fixed clock");
            return true;
        }

        value = null!;
        return false;
    }
}
```

```csharp
builder.AddData(data => data.AddValueResolver(new FixedClockResolver()));
```

The second argument of `ProtoDataResolvedValue` is the source shown in `Explain()` and the trace.

## Keeping values out of the trace

Built values are recorded in ProtoTrace. Redact the sensitive ones — the trace still shows *where* the value came from, just not the value:

```csharp
data.For<User>().Redact(x => x.AccessToken);   // one member
data.RedactValueType<Password>();              // every value of a type
```
