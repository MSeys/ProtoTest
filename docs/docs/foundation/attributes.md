---
sidebar_position: 5
title: Attributes
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

Here's the environment attribute from the sample suite, in full:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SampleEnvironmentAttribute : ProtoAttribute
{
    public SampleEnvironmentAttribute()
    {
        Order = -200;
    }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var environmentName = $"test-{context.TestId}";
        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .Body(new CreateEnvironmentRequest(environmentName))
            .PostAsync("/test-support/environments");

        response.ShouldHaveStatus(HttpStatusCode.Created);
        var environment = response.ReadAsJson<EnvironmentResponse>()
            ?? throw new InvalidOperationException("The sample app returned no environment.");

        context.SetContext(new SampleEnvironmentContext(
            environment.Tenant,
            environment.Name,
            environment.ApiBaseUrl));
    }

    public override async Task AfterTestAsync(ProtoExecutionContext context)
    {
        var environment = context.TryContext<SampleEnvironmentContext>();
        if (environment is null) return;

        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .DeleteAsync("/test-support/environments/{tenant}", new { environment.Tenant });
        response.ShouldHaveStatus(HttpStatusCode.NoContent);
    }
}
```

And the user attribute that builds on it:

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class SampleUserAttribute : ProtoAttribute
{
    public SampleUserAttribute(string role = SampleRoles.Member)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Role = role;
        Order = -100;
    }

    public string Role { get; }

    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var environment = context.Context<SampleEnvironmentContext>();
        var email = $"{Role}.{context.TestId}@example.test";

        using var response = await context.Rest(SampleAppTargets.Api)
            .WithoutAuth()
            .Body(new CreateUserRequest(email, Role))
            .PostAsync("/test-support/environments/{tenant}/users", new { environment.Tenant });

        response.ShouldHaveStatus(HttpStatusCode.Created);
        var user = response.ReadAsJson<UserResponse>()
            ?? throw new InvalidOperationException("The sample app returned no user.");

        context.SetContext(new SampleUserContext(user.Id, user.Tenant, user.Email, user.Role, user.AccessToken));
    }
}
```

What makes these work well:

- **They publish typed state** with `SetContext`, so tests, authenticators and other attributes can read it.
- **They derive names from `context.TestId`**, so parallel tests never collide.
- **They use other ProtoTest clients** — attributes run after clients are created.
- **Their teardown tolerates partial setup** (`TryContext`, early return), because after a failed setup only completed components are rolled back.
- **The user attribute doesn't delete the user** — deleting the tenant already takes care of it.

## Ordering

Attributes run in ascending `Order` before the test and in reverse afterwards. Give a capability that others depend on a **lower** order:

| Attribute | `Order` |
| --- | --- |
| `[SampleEnvironment]` | −200 |
| `[SampleUser]` | −100 |
| `[LoginAs<T>]` | 0 |

Because `Order` is an `init` property, callers can override it where they apply the attribute:

```csharp
[SampleUser(SampleRoles.Member, Order = -150)]
```

Where the attribute is declared doesn't affect ordering: class-level and method-level attributes are sorted together by `Order`, and class-level ones come first only on a tie. Every test hook runs before every attribute.

## Class, method and inheritance

Attributes are collected from the test's class (including base classes) and from the method (including overridden base methods). Put suite-wide capabilities on the class and scenario-specific ones on the method.

Unlike REST's `[Auth<T>]` — where a method-level attribute *replaces* class-level ones — `ProtoAttribute`s simply accumulate. If both the class and the method have a `[SampleUser]`, both run.

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

## Attributes ProtoTest ships

| Attribute | Kind |
| --- | --- |
| [`[LoginAs<TStrategy>]`](../integrations/web/login.md) | `ProtoAttribute` — logs a browser session in |
| [`[Auth<T>]`](../integrations/rest/authentication.md), [`[GraphQLAuth<T>]`](../integrations/graphql/index.md#authentication) | metadata read by the REST/GraphQL hooks |
| `[RestClient]`, `[GraphQLClient]` | metadata choosing the default client |
| `[ProtoTest]`, `[ProtoTestFact]`, `[ProtoTestTheory]` | [runner](../runners/overview.md) entry points |

Most of the capabilities in a real suite are ones you write — that's the point.
