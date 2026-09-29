---
sidebar_position: 5
title: Attributes
description: "Turn setup into a named, reusable capability with a ProtoTest attribute, and compose it onto any test that needs it."
---

# Attributes

## What it is

An attribute is how you turn setup into a reusable, named **capability**, and compose it onto any test that needs it:

```csharp
[SampleEnvironment]                              // a fresh tenant, deleted afterwards
public sealed class BillingTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.BillingAdministrator)]   // a user with that role, in that tenant
    public async Task OpenInvoicesAreListed() { ... }
}
```

The test declares what it needs. The attribute provides it. No base fixture class is needed, and capabilities stay independent of each other.

## Attributes ProtoTest ships

| Attribute | Kind |
| --- | --- |
| [`[WebSession]`](../integrations/web/index.md#several-sessions-in-one-test) | `ProtoAttribute`: declares and optionally opens a browser session |
| [`[LoginAs<TStrategy>]`](../integrations/web/login.md) | `ProtoAttribute`: logs a browser session in |
| [`[Auth<T>]`](../integrations/rest/authentication.md) | metadata read by the HTTP hooks, one authenticator for REST and GraphQL, narrowed with `Protocols` |
| [`[SignedInAs]`](../integrations/rest/authentication.md#built-in-test-user) | `ProtoAttribute` plus HTTP auth metadata: declares the test user's name, roles and claims for the in-process application |
| `[Application("Name", "Protocol:Client")]` | `ProtoAttribute`: selects the application under test and, optionally, which client each protocol uses |
| [`[RequiresCapability(kind)]`](./skip-conditions.md) | `ProtoAttribute`: skips the test unless the host has the capability |
| [`[RequiresWorker<TProgram>]`](./skip-conditions.md#typed-conditions-for-the-host-composition) | `ProtoAttribute`: skips unless `AddWorkerHost<TProgram>()` hosts the worker |
| [`[RequiresServer(name)]`](./skip-conditions.md#typed-conditions-for-the-host-composition) | `ProtoAttribute`: skips unless the named `AddAspNetCoreServer` instance is composed |
| [`[RequiresApplication(name)]`](./skip-conditions.md#typed-conditions-for-the-host-composition) | `ProtoAttribute`: skips unless `AddApplication` declared the application |
| [`[RequiresInProcess]`](./skip-conditions.md) | `ProtoAttribute`: `[RequiresCapability("server")]` |
| `[ProtoTest]`, `[ProtoTestFact]`, `[ProtoTestTheory]` | [runner](../runners/overview.md) entry points |

Most of the capabilities in a real suite are ones you write. That is the point.

## How it works

Every attribute derives from `ProtoAttribute` and overrides what it needs:

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public abstract class ProtoAttribute : Attribute
{
    public int Order { get; init; } = 0;
    public virtual Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
    public virtual Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}
```

Attributes run in ascending `Order` before the test and in reverse afterwards. Give a capability that others depend on a **lower** order:

| Attribute | `Order` |
| --- | --- |
| `[SampleEnvironment]` | -200 |
| `[SampleUser]` | -100 |
| `[WebSession]` | -10 |
| `[LoginAs<T>]` | 0 |

Because `Order` is an `init` property, callers can override it where they apply the attribute:

```csharp
[SampleUser(SampleRoles.Member, Order = -150)]
```

Where the attribute is declared does not affect ordering: class-level and method-level attributes are sorted together by `Order`, and class-level ones come first only on a tie. Every test hook runs before every attribute, as [Host and lifecycle](./lifecycle.md) describes. The [reserved `Order` bands](./hooks.md#reserved-order-bands) name the range each kind of capability belongs in.

Attributes are collected from the test's class (including base classes) and from the method (including overridden base methods). Put suite-wide capabilities on the class and scenario-specific ones on the method.

Unlike `[Auth<T>]`, where a method-level attribute *replaces* class-level ones, `ProtoAttribute`s accumulate. If both the class and the method have a `[SampleUser]`, both run. The one exception is a composed attribute: the same declaration explicitly and through a composite runs once, see [Composite attributes](#composite-attributes).

### Composite attributes

When a capability always comes with another one, declare the group once as a **composite attribute**: an attribute that names the attributes it composes. The framework expands it wherever attributes are resolved.

```csharp
public sealed class NorthstarMemberAttribute(string planId = PlanIds.Free) : ProtoCompositeAttribute
{
    public string PlanId { get; } = planId;

    protected override IReadOnlyList<Attribute> Compose() =>
    [
        new NorthstarTenantAttribute(PlanId),         // provisions the tenant
        new AuthAttribute<NorthstarAuthenticator>(),  // carries the member's bearer token
    ];
}
```

A journey then carries one declaration, while the identity stays where it varies per test:

```csharp
[Application(NorthstarTargets.Api)]
[NorthstarMember(PlanIds.Growth)]
public sealed class BillingJourney
{
    [ProtoTest]
    [SignedInAs]
    public async Task UsageIsMeteredAgainstThePlanAllowance() { ... }
}
```

How the expansion behaves:

- **Order still decides.** Composed attributes run like declarations the test made itself: ascending `Order` before the test, reverse after. On a tie a composed attribute runs before the composite that declared it, so the composite's own `BeforeTestAsync` sees its dependencies. The composite's own behavior runs at its `Order` like any attribute.
- **Recursive.** A composite may compose another composite. A cycle throws naming the chain when the attributes are resolved, before the lifecycle starts.
- **Once per declaration.** An attribute that appears both explicitly and through a composite runs once. Two declarations are the same when their attribute type and every public property value match; collections compare element-wise and `Order` is a property, so an override makes a different declaration. The explicit declaration wins over the composed copy.
- **Visible in the trace.** Every composed attribute gets its own `attribute.before` and `attribute.after` entry, and a composite's entries carry `attribute.composed` naming the types it expanded to, so the run shows what actually executed.

`Compose()` returns `Attribute` instances, so a composite can also group metadata-only attributes that hooks read. `[Auth<T>]` is not a lifecycle attribute, and a composite that declares it reaches the HTTP auth hook the same way an explicit declaration does. `Compose()` must be a deterministic factory: the framework calls it once per attribute instance and runs the instances it returns.

## How to use it

Here is the shape of the environment attribute from the sample suite:

```csharp
public sealed class SampleEnvironmentAttribute : ProtoAttribute
{
    public SampleEnvironmentAttribute() => Order = -200;

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .Body(new CreateEnvironmentRequest($"test-{context.TestId}"))
            .PostAsync("/test-support/environments");

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var environment = response.ReadAsJson<EnvironmentResponse>()!;
        context.SetContext(new SampleEnvironmentContext(environment.Tenant, environment.Name));
    }
}
```

Its teardown tolerates a setup that never got that far:

```csharp
public override async Task AfterTestAsync(ProtoExecutionContext context)
{
    if (context.TryResolve<SampleEnvironmentContext>() is not { } environment) return;
    using var response = await context.Rest(SampleAppTargets.Api)
        .WithoutAuth()
        .DeleteAsync("/test-support/environments/{tenant}", new { environment.Tenant });
    response.Should.HaveHttpStatus(HttpStatusCode.NoContent);
}
```

And the user attribute that builds on it:

```csharp
public sealed class SampleUserAttribute : ProtoAttribute
{
    public SampleUserAttribute(string role = SampleRoles.Member)
    {
        Role = role;
        Order = -100;
    }

    public string Role { get; }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var environment = context.Resolve<SampleEnvironmentContext>();
        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .Body(new CreateUserRequest($"{Role}.{context.TestId}@example.test", Role))
            .PostAsync("/test-support/environments/{tenant}/users", new { environment.Tenant });

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        context.SetContext(response.ReadAsJson<UserResponse>()!);
    }
}
```

The sample suite's environment and user attributes are `SampleEnvironmentAttribute` and `SampleUserAttribute` (see below). The learning sample groups its pair as `NorthstarMemberAttribute` in `samples/Northstar.ProtoTest`.

What makes these work well:

- **They publish typed state** with `SetContext`, so tests, authenticators and other attributes can read it.
- **They derive names from `context.TestId`**, so parallel tests never collide.
- **They use other ProtoTest clients.** Attributes run after clients are created.
- **Their teardown tolerates partial setup** (`TryResolve`, early return), because after a failed setup only completed components are rolled back.
- **The user attribute does not delete the user.** Deleting the tenant already takes care of it.

### Services in attributes

C# attributes are created by reflection, so they cannot take constructor-injected services. Resolve what you need from the context instead:

```csharp
public override async Task BeforeTestAsync(ProtoExecutionContext context)
{
    var tenants = context.Service<ITenantAdministration>();
    // ...
}
```

Attribute arguments must be compile-time constants. Pass a *name*, such as a role, a persona or a fixture key, and look the real values up at runtime.

### Skip conditions

An attribute can also stop a test before the lifecycle starts. Implement `IProtoSkipCondition` on a `ProtoAttribute`, or reuse the shipped conditions:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Store, Reason = "The suite does not own the store.")]
[RequiresInProcess]
```

Conditions run before `StartTestAsync`, so a skipped test never creates a context. [Skip conditions](./skip-conditions.md) covers evaluation, the per-runner behaviour and the limits.

## What the trace shows

- One `attribute.before` and `attribute.after` entry per attribute, in the order they ran.
- A composite's steps carry `attribute.composed` naming the attributes it expanded to, and each composed attribute has its own steps.
- `SetContext` records the value as traced state on the context entity, so a reader sees what an attribute published. A failed `Resolve` writes a `context.resolve` event.
- An attribute that fails during setup stops the sequence. The trace shows a `Rollback` phase where only the components that completed run their teardown.
- A skip condition runs before the lifecycle starts, so a skipped test has no test record and no attribute steps at all. The reason appears in the runner's own output.

## Limits

- C# attributes are created by reflection: no constructor injection. Resolve services from the context inside `BeforeTestAsync` and `AfterTestAsync`, and pass only compile-time constants to constructors.
- Only `Order` decides sequencing, and ties put class-level attributes first. A failed setup rolls back only the components that completed, so teardown must tolerate partial state (`TryResolve`, early return).
- A composite deduplicates by attribute type plus public property values. An attribute that hides configuration in private state cannot be recognized as the same declaration, so give it a public property. `Compose()` is called once per attribute instance, and a composite must not compose itself, directly or through another composite.
- A skip condition only sees [registered capabilities](./skip-conditions.md): an integration that is not configured makes its tests skip, which is the point.
- `[RequiresInProcess]` inspects capability registration only. It does not itself look at `BaseUrl`. Integrations make that registration honest: `AddAspNetCoreServer` drops its `server` capability when an address is configured, so the condition skips a published run.
