---
sidebar_position: 4
title: Authentication
---

# Authentication

REST and GraphQL share one authentication model, defined in `ProtoTest.Http`. An **authenticator** gets the outgoing `HttpRequestMessage` just before it's sent and adds whatever the API needs.

```csharp
public interface IProtoHttpAuthenticator
{
    ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ProtoHttpAuthenticationContext(
    HttpRequestMessage Request,
    ProtoExecutionContext Test,
    string ClientName);
```

Because the context carries the running test, an authenticator can read typed state that an attribute set up earlier — which is how a test gets a fresh user without any header code.

## Built-in authenticators

| Authenticator | Constructor | Adds |
| --- | --- | --- |
| `BearerTokenAuthenticator` | `(string token)` | `Authorization: Bearer <token>` |
| `BasicAuthAuthenticator` | `(string username, string password)` | `Authorization: Basic <base64>` |
| `ApiKeyAuthenticator` | `(string keyName, string keyValue, ApiKeyLocation location = ApiKeyLocation.Header)` | a header, or a query parameter with `ApiKeyLocation.Query` |

## Applying it

### Per request

```csharp
await Proto.Context.Rest()
    .Auth<BearerTokenAuthenticator>("orders-token")
    .GetAsync("/api/orders");

await Proto.Context.Rest()
    .Auth(new ApiKeyAuthenticator("X-Api-Key", apiKey))
    .GetAsync("/api/orders");
```

### Per test or class, with an attribute

```csharp
[Application("Api")]
[Auth<BearerTokenAuthenticator>("orders-token")]
public class OrderTests
{
    [ProtoTest]
    public async Task Lists_orders()
    {
        // Authorization is already applied.
        var response = await Proto.Context.Rest().GetAsync("/api/orders");
        response.ShouldHaveHttpStatus(HttpStatusCode.OK);
    }
}
```

`[Application("Api")]` selects the application the test targets; `Rest()` uses that application's default REST client. Bind a different client with `[Application("Api", "Rest:Billing")]`.

### Opting out

```csharp
await Proto.Context.Rest()
    .WithoutAuth()
    .GetAsync("/api/health");
```

This is how you test that an endpoint rejects anonymous callers, or send a deliberately wrong token:

```csharp
using var response = await Proto.Context.Rest()
    .WithoutAuth()
    .Header("Authorization", $"Bearer {user.AccessToken}")
    .Header("X-Tenant", "competitor-tenant")
    .GetAsync("/api/control-plane");

response
    .ShouldHaveHttpStatus(HttpStatusCode.Forbidden)
    .ShouldMatchShape(new { error = "tenant-access-denied" });
```

## Precedence

When several of these apply, this is what wins:

1. **`[Application]`** on the method beats the one on the class (and its client bindings replace the class's).
2. **`[Auth<T>]` on the method replaces the class-level ones entirely** — they don't merge.
3. Several `[Auth<T>]` attributes at the same level are ordered by their `Order` property and **composed**: each runs in turn on the same request.
4. **A per-request `.Auth(...)` overrides** whatever the attributes resolved, and **`.WithoutAuth()` clears it**.

An authenticator is created once per request builder and reused if that builder sends more than once. A skipped authentication shows up in the trace as `auth.skip`, an applied one as `auth.apply`.

## Writing your own

Most real suites need one. Here's the one from the sample app, which authenticates as whichever user the test's `[SampleUser]` attribute created:

```csharp
using System.Net.Http.Headers;
using ProtoTest.Http;

public sealed class SampleUserAuthenticator : IProtoHttpAuthenticator
{
    public ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var environment = context.Test.Resolve<SampleEnvironmentContext>();
        var user = context.Test.Resolve<SampleUserContext>();

        context.Request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);
        context.Request.Headers.Add("X-Tenant", environment.Tenant);
        return ValueTask.CompletedTask;
    }
}
```

### Constructor arguments and services

`[Auth<T>(args)]` and `.Auth<T>(args)` construct `T` with `ActivatorUtilities`, so the constructor can mix **positional arguments** from the attribute with **services** from the test's DI scope — including `ProtoExecutionContext` itself:

```csharp
public sealed class TenantTokenAuthenticator(
    string tenant,                 // from [Auth<TenantTokenAuthenticator>("tenant-a")]
    ITokenService tokens)          // resolved from DI
    : IProtoHttpAuthenticator
{
    public async ValueTask AuthenticateAsync(
        ProtoHttpAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        var token = await tokens.GetTokenAsync(tenant, cancellationToken);
        context.Request.Headers.Authorization = new("Bearer", token);
    }
}
```

This keeps secrets out of attribute metadata: the attribute carries only a name, and the authenticator looks up the real value.

The same authenticator serves GraphQL too: [`[Auth<T>]`](../graphql/index.md#authentication) applies to every HTTP-based protocol the application exposes, and its `Protocols` property narrows it when an application exposes both and you only want one.
