---
sidebar_position: 6
title: Logging in
description: "Log in once per test the way your application does — a login page, SSO, an API token or saved storage state — as a reusable capability."
---

# Logging in

Almost every browser test starts by logging someone in, and almost every application does that differently: a login page, an SSO redirect, an API call plus an injected token, saved storage state. ProtoTest doesn't pick one for you. You write a **login strategy** — as many as you need — and apply it with an attribute.

```csharp
[ProtoTest]
[LoginAs<BackOfficeLogin>("billing.admin")]
public async Task Billing_admin_sees_open_invoices()
{
    var invoices = Proto.Context.Web().Page<InvoicesPage>();
    await invoices.OpenAsync("/invoices");
    await invoices.Heading.Should.BeVisibleAsync();
}
```

## Writing a strategy

```csharp
public interface IWebLoginStrategy
{
    ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default);
}

public sealed record WebLoginContext(
    ProtoExecutionContext Execution,   // the running test
    WebSession Web,                    // the session to log in
    string Persona);                   // the name from the attribute
```

### Through the real login page

```csharp
public sealed class BackOfficeLogin(ICredentialStore credentials) : IWebLoginStrategy
{
    public async ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default)
    {
        var secret = credentials.PasswordFor(context.Persona);

        var login = context.Web.Page<LoginPage>();
        await login.OpenAsync("/login", cancellationToken);

        await login.Form.Flow("Sign in")
            .Fill(form => form.Username, context.Persona)
            .Fill(form => form.Password, secret)
            .Click(form => form.Submit)
            .RunAsync(cancellationToken);

        await login.Form.Status.Should.HaveTextAsync("Signed in", cancellationToken: cancellationToken);
    }
}
```

The demo's strategy against the sample application's own login page is in [StandaloneSampleApp.cs](../../../../samples/ProtoTest.Demo/Support/StandaloneSampleApp.cs).

### Using state another attribute created

Strategies see the test's typed state, so they can log in as a user that an earlier attribute provisioned:

```csharp
public sealed class ProvisionedUserLogin : IWebLoginStrategy
{
    public async ValueTask LoginAsync(WebLoginContext context, CancellationToken cancellationToken = default)
    {
        var user = context.Execution.Resolve<SampleUserContext>();
        // ... sign in with user.Email / user.AccessToken
    }
}
```

```csharp
[ProtoTest]
[SampleUser(SampleRoles.BillingAdministrator)]     // Order = -100, runs first
[LoginAs<ProvisionedUserLogin>("billing admin")]   // Order = 0, runs after
public async Task ...
```

`LoginAs` has the default `Order` of `0`; see [Attributes](../../foundation/attributes.md) for how ordering works.

## The attribute

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class LoginAsAttribute<TStrategy>(string persona, params object[] constructorArgs) : ProtoAttribute
    where TStrategy : class, IWebLoginStrategy
{
    public string Persona { get; }
    public string Session { get; init; } = "Default";
}
```

- **`persona`** is a name, not a credential. Keep secrets in your strategy or a service it depends on — attribute arguments are compiled into your assembly metadata.
- **Constructor arguments** after the persona are passed to the strategy's constructor, and any remaining parameters are resolved from dependency injection through `ProtoAuthenticatorFactory.Create<TStrategy>(context, constructorArgs)`, which also injects the `ProtoExecutionContext` itself:

  ```csharp
  [LoginAs<TenantLogin>("admin", "tenant-a")]

  public sealed class TenantLogin(string tenant, ICredentialStore credentials) : IWebLoginStrategy { … }
  ```

- **`Session`** picks which [named session](./index.md#several-sessions-in-one-test) to log in, so one test can have two different people signed in (it defaults to `"Default"`).

## Sessions and ordering

A logged-in session must exist first. `[WebSession]` declares one during setup and can open a start URL; `Application` selects the application, `Open` the address, and the attribute's fixed `Order = -10` runs it before `[LoginAs]`:

```csharp
[WebSession("Admin", Application = "ControlPlane", Open = "/back-office")]   // address from ProtoTest:Applications:ControlPlane:BaseUrl
[WebSession("Customer")]
[LoginAs<BackOfficeLogin>("billing.admin", Session = "Admin")]
[LoginAs<StorefrontLogin>("customer@example.test", Session = "Customer")]
public async Task ...
```

`ProtoTest:Web:Sessions:{name}:Open` overrides the attribute's `Open`, so code stays environment-agnostic; started infrastructure settings win over static configuration. A relative `Open` resolves against the session's `BaseUrl`.

## Tracing

The login runs during setup as a `web.login` trace operation named `Login · {persona} [{session}]`, carrying `web.session`, `web.login.persona` and `web.login.strategy`. If it fails, the test fails in setup and everything that already ran is [rolled back](../../foundation/lifecycle.md#when-setup-fails).

## Next

- [Waits and middleware](./middleware.md) — wait for the application after a login.
- [Diagnostics and artifacts](./diagnostics.md) — what a failed login leaves in the trace.
