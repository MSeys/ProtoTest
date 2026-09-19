---
sidebar_position: 4
title: Attributes
description: "Turn setup into a named, reusable capability with a ProtoTest attribute, and compose it onto any test that needs it."
---

# Attributes

Attributes are how you turn setup into a reusable, named **capability**, and compose it onto any test that needs it:

```csharp
[SampleEnvironment]                              // a fresh tenant, deleted afterwards
public sealed class BillingTests
{
    [ProtoTest]
    [SampleUser(SampleRoles.BillingAdministrator)]   // a user with that role, in that tenant
    public async Task ...
}
```

The test says *what* it needs. The attribute knows *how*. Nothing is inherited from a base fixture class, and two capabilities never have to know about each other.

## Writing one

Derive from `ProtoAttribute` and override what you need:

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public abstract class ProtoAttribute : Attribute
{
    public int Order { get; init; } = 0;
    public virtual Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
    public virtual Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}
```

Here's the shape of the environment attribute from the sample suite:

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

The sample suite's real pair is [`NorthstarTenantAttribute` and `SignedInAsAttribute`](../../../samples/Northstar.ProtoTest/NorthstarAttributes.cs).

What makes these work well:

- **They publish typed state** with `SetContext`, so tests, authenticators and other attributes can read it.
- **They derive names from `context.TestId`**, so parallel tests never collide.
- **They use other ProtoTest clients** — attributes run after clients are created.
- **Their teardown tolerates partial setup** (`TryResolve`, early return), because after a failed setup only completed components are rolled back.
- **The user attribute doesn't delete the user** — deleting the tenant already takes care of it.

## Ordering

Attributes run in ascending `Order` before the test and in reverse afterwards. Give a capability that others depend on a **lower** order:

| Attribute | `Order` |
| --- | --- |
| `[SampleEnvironment]` | −200 |
| `[SampleUser]` | −100 |
| `[WebSession]` | −10 |
| `[LoginAs<T>]` | 0 |

Because `Order` is an `init` property, callers can override it where they apply the attribute:

```csharp
[SampleUser(SampleRoles.Member, Order = -150)]
```

Where the attribute is declared doesn't affect ordering: class-level and method-level attributes are sorted together by `Order`, and class-level ones come first only on a tie. Every test hook runs before every attribute.

## Class, method and inheritance

Attributes are collected from the test's class (including base classes) and from the method (including overridden base methods). Put suite-wide capabilities on the class and scenario-specific ones on the method.

Unlike `[Auth<T>]` — where a method-level attribute *replaces* class-level ones — `ProtoAttribute`s simply accumulate. If both the class and the method have a `[SampleUser]`, both run.

## Services in attributes

C# attributes are created by reflection, so they can't take constructor-injected services. Resolve what you need from the context instead:

```csharp
public override async Task BeforeTestAsync(ProtoExecutionContext context)
{
    var tenants = context.Service<ITenantAdministration>();
    // ...
}
```

Attribute arguments must be compile-time constants. Pass a *name* — a role, a persona, a fixture key — and look the real values up at runtime.

## Skip conditions

An attribute can also stop a test before the lifecycle starts. Implement `IProtoSkipCondition` on a `ProtoAttribute`, or reuse the shipped conditions:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Store, Reason = "The suite does not own the store.")]
[RequiresInProcess]
```

Conditions run before `StartTestAsync`, so a skipped test has no context and no teardown, and the runner reports the reason. MSTest has no public dynamic-skip API: it returns an ignored result and carries the reason on the display name and log output. Conditions are **adapter-opt-in**: a runner that does not evaluate them simply runs the test, and evaluation happens in the order the runner resolves attributes, not in `Order` — NUnit, for example, checks class-level conditions before method-level ones in reflection order. [Skip conditions](./skip-conditions.md) covers evaluation, the per-runner behaviour and the limits.

## Attributes ProtoTest ships

| Attribute | Kind |
| --- | --- |
| [`[WebSession]`](../integrations/web/index.md#several-sessions-in-one-test) | `ProtoAttribute` — declares and optionally opens a browser session |
| [`[LoginAs<TStrategy>]`](../integrations/web/login.md) | `ProtoAttribute` — logs a browser session in |
| [`[Auth<T>]`](../integrations/rest/authentication.md) | metadata read by the HTTP hooks — one authenticator for REST and GraphQL, narrowed with `Protocols` |
| `[Application("Name", "Protocol:Client")]` | `ProtoAttribute` — selects the application under test and, optionally, which client each protocol uses |
| [`[RequiresCapability(kind)]`](./skip-conditions.md) | `ProtoAttribute` — skips the test unless the host has the capability |
| [`[RequiresInProcess]`](./skip-conditions.md) | `ProtoAttribute` — `[RequiresCapability("server")]` |
| `[ProtoTest]`, `[ProtoTestFact]`, `[ProtoTestTheory]` | [runner](../runners/overview.md) entry points |

Most of the capabilities in a real suite are ones you write — that's the point.

## Limits

- C# attributes are created by reflection: no constructor injection. Resolve services from the context inside `BeforeTestAsync`/`AfterTestAsync`, and pass only compile-time constants to constructors.
- Only `Order` decides sequencing, and ties put class-level attributes first; a failed setup rolls back only the components that completed, so teardown must tolerate partial state (`TryResolve`, early return).
- A skip condition only sees [registered capabilities](./skip-conditions.md): an integration that is not configured makes its tests skip, which is the point.
- `[RequiresInProcess]` inspects capability registration only — it does not look at `BaseUrl`.
