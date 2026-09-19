---
sidebar_position: 1
title: Overview
description: "Build test objects with deterministic defaults, so a test only states the values it is about, and create them in the system under test."
---

# Data

`ProtoTest.Data` builds test objects with deterministic defaults, so the only values you write in a test are the ones the test is *about* — and it can hand those objects to your application to create them for real.

Numbers, enums, dates and your own value objects are never invented: if nothing supplies a member, the build fails with a `ProtoDataException` naming it, rather than a silently wrong `0`.

```csharp
var invoice = Proto.Context.Data().For<Invoice>()
    .With(x => x.Total, 125m)
    .With(x => x.Status, InvoiceStatus.Overdue)
    .Build();
```

A reader sees immediately that this test cares about an overdue invoice of 125. Every other property — the id, the customer, the currency — comes from [defaults you configure once](./defaults.md).

## Install

```bash
dotnet add package ProtoTest.Data --prerelease
```

ProtoTest targets .NET 8, 9 and 10; the template defaults to `net10.0` unless `-f` is passed. `ProtoTest.Data` depends only on `ProtoTest.Core`.

## Registering

```csharp
builder
    .AddData(data => data.AddDefaults<InvoiceDataDefaults>())
    .AddDataProvisioner<Invoice, InvoiceProvisioner>();
```

```csharp
public static IProtoHostBuilder AddData(
    this IProtoHostBuilder builder,
    Action<ProtoDataConfiguration>? configure = null);

public static IProtoHostBuilder AddDataProvisioner<T, TProvisioner>(this IProtoHostBuilder builder)
    where TProvisioner : class, IProtoDataProvisioner<T>;

public static IProtoHostBuilder AddDataProvisioner<TInput, TResult, TProvisioner>(this IProtoHostBuilder builder)
    where TProvisioner : class, IProtoDataProvisioner<TInput, TResult>;
```

`AddData` registers the `Data` capability (`ProtoCapabilityKinds.Data`) and a scoped `IProtoData` — one per test. Repeats compose onto one registry: each `AddData` callback runs while the capability and service registrations dedupe. Registering the same member, type or factory twice is **not** a repeat — it throws `ProtoDataException` naming both sources, so two modules cannot silently fight over a value. A second `AddDataProvisioner` with the same implementation type is a no-op; two *different* provisioners for one input/result pair both register and fail later, when that pair is used.

## Options and keys

`ProtoTest.Data` has no options type and no `ProtoTest:Data` configuration section. Everything is configured through the `AddData` callback:

| Entry point | What it configures |
| --- | --- |
| `data.AddDefaults<TModule>()` | a defaults module with a public parameterless constructor |
| `data.AddDefaultsFromAssembly(assembly)` | every public, concrete, non-generic module in an assembly, ordered by full type name (ordinal) |
| `data.For<T>().Default(...)` | member defaults, and domain factories with `ConstructUsing(...)` |
| `data.Values.Use<T>(...)` | a provider for every member of a type |
| `data.AddValueResolver(...)` | convention resolvers, run in registration order |
| `data.RedactValueType<TValue>()` | every resolved value of a type, in ProtoTrace |

[Defaults](./defaults.md) has the precedence order and the full surface, every signature included.

## Context API

```csharp
IProtoData data = Proto.Context.Data();
```

| Member | |
| --- | --- |
| `For<T>()` | starts a `ProtoDataObjectBuilder<T>` |
| `Ref<T>(identity = null)` | resolves a value `CreateAsync` provisioned earlier in this test, from the identity map |

On the builder:

| Member | |
| --- | --- |
| `With(member, value)` | sets a scenario-relevant member; returns the same builder |
| `Explain()` | resolves and describes every value without constructing the object |
| `Build()` / `BuildMany(count, configure)` | constructs in memory only |
| `CreateAsync()` / `CreateAsync<TResult>()` | builds, provisions through the registered provisioner, and returns the application's value |
| `CreateManyAsync(count, configure)` / `CreateManyAsync<TResult>(...)` | the same for `count` independently resolved objects |

## Quick start

```csharp
var project = Proto.Context.Data()
    .For<CreateProjectRequest>()
    .With(request => request.Name, "atlas")
    .Build();
```

`Build()` is enough when the test only needs an object; when the application must actually create it, use `CreateAsync` and a [provisioner](./provisioners.md).

## Going further

### Several at once

The `configure` callback receives each item's builder and its zero-based index:

```csharp
var projects = Proto.Context.Data().For<CreateProjectRequest>()
    .BuildMany(3, (project, index) => project.With(x => x.Name, $"atlas-{index}"));

var members = await Proto.Context.Data()
    .For<InviteMemberRequest>()
    .CreateManyAsync<MembershipResponse>(7);
```

Values set with `With` before `BuildMany` apply to every item.

### Constructors

Objects are created through their single public constructor (parameters matched to properties by name, ignoring case) or their parameterless one; remaining writable properties are set afterwards. Records work naturally through their primary constructor. Multiple public constructors without a parameterless route, a `With` on a member the chosen route cannot assign, and a non-writable property that is named in `With` all throw `ProtoDataException`. For types that protect their invariants, register a [domain factory](./defaults.md#domain-factories) instead.

### The identity map

`CreateAsync` and `CreateManyAsync` results are tracked per test under the identity string the provisioner returned, and `Ref<T>` resolves them again — typically to wire a foreign key:

```csharp
var projects = await Proto.Context.Data()
    .For<CreateProjectRequest>()
    .CreateManyAsync<ProjectResponse>(2);

var first = Proto.Context.Data().Ref<ProjectResponse>(projects[0].Id);
```

- Matching is by requested type (`entry.Value is T`) and, when an identity is given, by `StringComparison.Ordinal` equality — identities are case-sensitive.
- Zero matches or more than one match throw `ProtoDataException` with guidance: pass an identity when several values of the type exist.
- Only `CreateAsync` / `CreateManyAsync` results are in the map; `Build()` and `BuildMany()` values are never referenceable.
- `IProtoData` is scoped to one test, so the map cannot reach data provisioned by another test. `ProtoDataValueContext.Ref<T>(identity)` exposes the same lookup to defaults.

### Explain

When a value surprises you, ask where it came from — before constructing anything:

```csharp
var plan = Proto.Context.Data().For<Invoice>()
    .With(x => x.Total, 125m)
    .Explain();

foreach (var value in plan.Values)
    Console.WriteLine($"{value.MemberName} = {value.Value}  [{value.SourceKind}]");
```

Each `ProtoDataValueExplanation` carries `MemberName`, `ValueType`, `Value`, `SourceKind` and `Source` — for defaults, the module that registered them. `SourceKind` is one of `Explicit`, `MemberDefault`, `TypeProvider`, `CustomResolver`, `BuiltIn` or `ConstructorDefault`. For a factory type, `Explain()` lists only the explicit `With(...)` values plus the construction source, because the factory resolves its inputs when `Build()` runs.

## Tracing and coverage

Every builder operation is traced with source `ProtoTest.Data`:

| Operation | Notes |
| --- | --- |
| `data.explain`, `data.build` | carry `data.type`, `data.object_sequence`, `data.member_count` and `data.construction_source` (`Reflection` or the factory source) |
| `data.create`, `data.create_many` | parent the `data.provision` operation; `data.create` adds `data.identity` |
| `data.build_many` | adds `data.type` and `data.count` |
| `data.create_many` | adds `data.input_type`, `data.result_type` and `data.count` |
| `data.provision` | adds `data.input_type`, `data.result_type`, `data.provisioner`, `data.identity`, `data.owned` and `data.value_id` |
| `data.cleanup` | runs in the release phase with `data.type`, `data.identity`, `data.provisioner` |

Each resolved member writes a `data.value.resolve` event under the build or explain operation, with `data.type`, `data.member`, `data.value_type`, `data.value`, `data.redacted`, `data.source_kind` and `data.source`. An unresolved member writes the same event with `data.source_kind = "Unresolved"` before the exception is thrown.

Provisioned values are tracked as a `value` item whose id is `{type}:{id}` — the user-facing form in `data.value_id` is `value:{type}:{id}`, for example `value:invoice_line:INV-1`. The type segment is the CLR type name in snake_case with generic arity dropped (`Envelope<InvoiceLine>` becomes `envelope`); without an identity the segment ends in `#{n}`.

The package emits no observations and ships no coverage collector — its evidence lives in ProtoTrace. Redaction protects that trace graph only; it says nothing about application logs or HTTP bodies.

## Skip

The capability is name `"Data"`, kind `data` (`ProtoCapabilityKinds.Data`). Skip with:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Data)]
```

There are no package-specific skip attributes. See [Skip conditions](../../foundation/skip-conditions.md).

## Limits

- **No invented semantics.** Numbers, enums, booleans, dates and project value objects are never generated; an unresolved member fails with the member's name.
- **Reflection needs a public constructor.** Multiple public constructors without a parameterless one is an error; `With` must target a settable property or be consumed by a registered factory.
- **The identity map is per test and read-only for `Build`.** Values built in memory are not referenceable, and another test's provisioned data is out of reach.
- **Ref identity matching is case-sensitive** (`StringComparison.Ordinal`).
- **Redaction is best-effort and trace-only.** Reference cycles are cut to `[circular]`, and nothing else is redacted — not logs, not HTTP bodies, not reports.
- **No retry or transaction semantics.** A `Cleanup` that fails is aggregated by the core release path like any other test resource.

## Links

- [Defaults](./defaults.md) — modules, precedence, factories, resolvers and redaction.
- [Provisioners](./provisioners.md) — creating data in your application, `Ref<T>` and cleanup.
- [SQL](../sql/index.md) — a per-test database connection for the created rows.
- The demo creates and checks data end to end in [`samples/ProtoTest.Demo/SheetsJourney.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/SheetsJourney.cs), with defaults in [`samples/ProtoTest.SampleApp.Testing/NorthstarData.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.SampleApp.Testing/NorthstarData.cs) and registration in [`samples/ProtoTest.Demo/Setup.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs).
