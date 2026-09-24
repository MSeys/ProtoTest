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

You can also configure inline in `AddData(data => …)`; the trace then reports the source as `"Host configuration"`, while a module's values are attributed to the module's full type name.

## The configuration surface

| Member | |
| --- | --- |
| `data.Values` | a `ProtoDataValueConfiguration` for providers that apply to every member of a type |
| `data.For<T>()` | a `ProtoDataTypeConfiguration<T>` for one object type |
| `data.RedactValueType<TValue>()` | redacts every resolved value of the type in ProtoTrace |
| `data.AddValueResolver(resolver)` | adds an `IProtoDataValueResolver` after exact member and type providers |
| `data.AddDefaults<TModule>()` | adds one module (`where TModule : IProtoDataDefaultsModule, new()`) |
| `data.AddDefaultsFromAssembly(assembly)` | discovers modules in an assembly and adds them in full-name order |
| `For<T>().Default(member, value)` | a constant member default |
| `For<T>().Default(member, provider)` | a member default computed from a `ProtoDataValueContext` |
| `For<T>().ConstructUsing(factory)` | builds the type through a domain factory instead of reflection |
| `For<T>().Redact(member)` | hides one member's value in ProtoTrace while keeping its provenance |
| `Values.Use<T>(provider)` | supplies a value for every member of `T` |

`AddDefaultsFromAssembly` takes public, concrete, non-generic classes assignable to `IProtoDataDefaultsModule` with a public parameterless constructor, and runs them ordered by `Type.FullName` with `StringComparer.Ordinal`. Registering the same member, type or factory twice throws `ProtoDataException` naming both sources; there is no last-one-wins, so two modules cannot silently fight over a value. Resolvers are the exception: they may repeat and all run in order.

## Where a value comes from

For each member, the first of these that applies wins:

1. **`With(...)`** in the test.
2. A **member default** — `data.For<T>().Default(x => x.Member, …)`, resolved by walking the target type and its base types.
3. A **type provider** — `data.Values.Use<TValue>(…)`, matched on the exact type only.
4. A **custom resolver** — `IProtoDataValueResolver`, in registration order; a throwing resolver is wrapped in `ProtoDataException`.
5. A **safe built-in**: `null` for a nullable member or a nullable-annotated reference, a generated string for `string`, a generated `Guid`, and an empty array or list for array, `IEnumerable<T>`, `IReadOnlyCollection<T>`, `IReadOnlyList<T>`, `ICollection<T>`, `IList<T>` and `List<T>`.
6. The constructor parameter's **default value**, on the constructor route only.

If none apply, `ProtoDataException` names the member. Numbers, enums, booleans, dates and your own value objects are deliberately *not* on the built-in list — provide them explicitly or through a default.

## Member defaults

```csharp
data.For<InviteMemberRequest>()
    .Default(request => request.Email,
        context => $"member-{context.TestId}-{context.ObjectSequence:D4}@example.test");
```

Use the provider overload whenever the value must be unique — a constant email address breaks the moment two tests run in parallel.

## Type providers

A type provider supplies a value for **every** member of a type, on any object:

```csharp
data.Values.Use<Money>(_ => new Money(10m, "EUR"));
data.Values.Use<TenantId>(context => new TenantId(context.NextGuid()));
```

## The value context

Both kinds of provider, and every member default, receive a `ProtoDataValueContext`:

| Member | |
| --- | --- |
| `TestId` | the running test's id |
| `ObjectSequence` | the object's position among objects built in this test |
| `TargetType`, `ValueType`, `MemberName` | what is being resolved |
| `Services` | the test's service provider |
| `NextGuid()` | a deterministic GUID |
| `NextString()` | a deterministic string like `Invoice.Reference-0001-00` |
| `Ref<T>(identity)` | a value provisioned earlier in this test, from the [identity map](./provisioners.md#refs-and-the-identity-map) |

`NextGuid()` is the first 16 bytes of a SHA-256 over `"{TestId}|{ObjectSequence}|{TargetType.FullName}|{MemberName}|{counter}"`, and `NextString()` is `"{TargetType.Name}.{MemberName}-{ObjectSequence:D4}-{counter:D2}"`. Each member resolution gets a fresh context, so the counter starts at 0 per member: the same test produces the same values on every run, while different tests never collide.

## Domain factories

When a type enforces invariants through a factory method, let ProtoTest call it instead of a constructor:

```csharp
data.For<Invoice>()
    .ConstructUsing(context => Invoice.Create(
        context.Value<InvoiceId>(nameof(Invoice.Id)),
        context.Value<Money>(nameof(Invoice.Total))));
```

`context.Value<TValue>(memberName)` resolves that member through the normal pipeline — so `With(x => x.Total, …)` in a test still flows into the factory. `ProtoDataConstructionContext` exposes `Services` too.

The factory rules:

- One factory per type. A second `ConstructUsing` for the same type throws.
- The factory should consume every input it needs through `Value<TValue>(...)`. A factory that does not consume an explicit `With` value throws, as does resolving the same member name as two different types.
- A factory returning `null` or an incompatible value throws; exceptions from inside the factory are wrapped with context.
- `Explain()` for a factory type lists only the explicit `With(...)` values and the construction source (`ConstructionSource`), because the factory resolves the rest when `Build()` runs.

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

Resolvers run after member and type providers, in registration order, and the second argument of `ProtoDataResolvedValue` is the source shown in `Explain()` and the trace.

## Keeping values out of the trace

Resolved values are recorded in ProtoTrace. Redact the sensitive ones — the trace still shows *where* the value came from, just not the value:

```csharp
data.For<User>().Redact(x => x.AccessToken);   // one member
data.RedactValueType<Password>();              // every value of a type
```

- A member is redacted when its declaring type, or any base type, matches the redacted type.
- A value is redacted when its declared or runtime type matches, including base types and interfaces; `Nullable<T>` is unwrapped, and a collection is redacted when its element type is.
- Non-redacted values are still walked as a graph: nested redacted members or types become `"[REDACTED]"`, and reference cycles become `"[circular]"`. The graph is copied only when something was redacted.

## Limits

- **Redaction protects ProtoTrace only.** It is best effort and says nothing about application logs, HTTP bodies or reports.
- **Factory inputs come from the same pipeline.** A factory cannot invent a value the pipeline would reject; unresolved members still fail.
- **Module discovery is shape-based.** A module needs a public parameterless constructor and a public concrete type; nested or generic modules are skipped.
- **Member defaults apply to the member's declaring type and its base types**, but a type provider matches the exact member type only — use a resolver for a family of types.

## Links

- [Data overview](./index.md) — the package page and quick start.
- [Provisioners](./provisioners.md) — `CreateAsync`, the identity map and cleanup.
- [Execution context](../../foundation/execution-context.md) — `Proto.Context` and what a test can reach.
