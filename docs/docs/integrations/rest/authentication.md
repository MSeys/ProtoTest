---
sidebar_position: 4
title: Authentication
description: "One authentication model for REST, GraphQL and gRPC: an authenticator adds what the API needs just before each request is sent."
---

# Authentication

REST and GraphQL share one authentication model. An authenticator adds credentials to the outgoing request just before it is sent.

```csharp
await Proto.Context.Rest()
    .Auth<BearerTokenAuthenticator>("orders-token")
    .GetAsync("/api/orders");
```

A method `[Auth]` replaces the class `[Auth]`; a per-request `.Auth()` beats attributes; `.WithoutAuth()` clears all. The full rules are under [Precedence](#precedence).

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

The context carries the running test. An authenticator can read state an attribute set earlier, so the test needs no header code.

## Built-in authenticators

The three shipped authenticators live in `ProtoTest.Http.Authenticators`:

| Authenticator | Constructor | Wire effect |
| --- | --- | --- |
| `BearerTokenAuthenticator` | `(string token)` | `Authorization: Bearer <token>` |
| `BasicAuthAuthenticator` | `(string username, string password)` | `Authorization: Basic <base64>` (UTF-8) |
| `ApiKeyAuthenticator` | `(string keyName, string keyValue, ApiKeyLocation location = ApiKeyLocation.Header)` | a header via `TryAddWithoutValidation`, or `?name=val` with `ApiKeyLocation.Query` |

`ApiKeyLocation` is `Header` or `Query`; the query form needs a request URI and throws `InvalidOperationException` if there is none.

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
        response.Should.HaveHttpStatus(HttpStatusCode.OK);
    }
}
```

`AuthAttribute<TAuthenticator>` applies to a class or method, allows several attributes at the same level, and inherits. Two properties shape it:

- `Order`: several attributes at the same level run in this order.
- `Protocols`: empty (the default) applies to every HTTP-based protocol the application exposes; otherwise it matches protocol names case-insensitively, for example `["GraphQL"]`.

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
    .Should.HaveHttpStatus(HttpStatusCode.Forbidden)
    .Should.MatchShape(new { error = "tenant-access-denied" });
```

## Precedence

When several of these apply, this is what wins:

1. **`[Application]`** on the method beats the one on the class (and its client bindings replace the class's).
2. **`[Auth<T>]` on the method replaces the class-level ones entirely**; they don't merge.
3. Several `[Auth<T>]` attributes at the same level are ordered by their `Order` property and **composed**: `ProtoCompositeHttpAuthenticator` runs each in turn on the same request, recording every handler under an `auth.handler.apply` operation with its `auth.type` and `client.name`. The composite's trace source is `ProtoTest.{protocol}`.
4. **A per-request `.Auth(...)` overrides** whatever the attributes resolved, and **`.WithoutAuth()` clears it**.

The REST and GraphQL lifecycle hooks (`ProtoHookOrder.Authentication`) resolve the attributes before each test and record a `Auth` entity state under the protocol name with `auth.source` (`method`, `class` or `none`), `auth.count` and `auth.types`. Per request, the applied authenticator is recorded on the request operation: `auth.outcome` is `applied` or `skipped`, with `auth.type` when applied. The trace records header names and the count, not header values (`http.header.value_recorded=false`).

## Built-in test user

When the application under test runs in-process and a test only needs "this request arrives as a signed-in user", the shipped test user replaces hand-written token plumbing. Declare the identity on the test:

```csharp
[ProtoTest]
[SignedInAs("alice", "Administrator", "Billing", Claims = new[] { "tenant=northstar" })]
public async Task AdministratorsCanCreateProjects()
{
    var user = Proto.Context.SignedInUser();   // alice · Administrator, Billing · tenant=northstar
    using var response = await Proto.Context.Rest().PostAsync("/api/projects");
}
```

| Piece | What it does |
| --- | --- |
| `[SignedInAs(name, roles…)]` | Declares the identity; roles follow the name, `Claims` adds `type=value` entries. Everything is constant attribute data - declare names, not secrets. |
| `context.SignIn(user)` | The same identity from the test body; it replaces the declared one for the rest of the test. |
| `context.SignedInUser()` | Reads it, or throws naming both ways to get one. |
| `TestUserAuthenticator` | The authenticator the declaration composes; it writes the `ProtoTest-User` header (Base64 JSON) on each request. |

The identity rides the existing pipeline: REST, GraphQL and gRPC requests carry it, and the protocol's `Auth` entity names `SignedInAsAttribute` among its authenticators. A method-level `[SignedInAs]` **composes** with a class-level `[Auth<T>]` instead of replacing it (the method-over-class replacement is for `[Auth<T>]`), so a class can own the "how" while a method names the "who".

### The app side

The header means something only to an application that opts in to read it. Add the shipped authentication inside the application's web-host callback:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>(webHost => webHost.AddTestUserAuthentication())
    .AddRest(rest => rest.AddClient("Api")));
```

It decodes the header into the application's `ClaimsPrincipal` - the name, a `ClaimTypes.Role` per role, and your custom claims - and becomes the application's default authentication scheme, so the application's own `[Authorize]`, `[Authorize(Roles = "…")]` and policies decide exactly as in production:

```csharp
[SignedInAs("alice", "admin")]
public async Task AdministratorsCanCreateProjects() { /* 201 */ }

[SignedInAs("alice", "viewer")]
public async Task ViewersCannotCreateProjects() { /* 403 */ }
```

The identity is per-test state: the next test starts with none, and parallel tests never share one. The trace records an `auth` entity with id `auth:user` (name, roles, claim **types**, application) plus an `auth.user.sign-in` event.

### Limits

- **In-process only.** The shipped handler is installed into the test host; against a published application the request goes out unchanged and the trace marks `auth.transport = inert` with the reason. A suite whose published environment accepts the identity declares its own `[Auth<T>]` authenticator that reads `context.SignedInUser()`.
- **It replaces the application's default authentication scheme.** Register `AddTestUserAuthentication` where a test user stands in for the application's own authentication, not in a suite whose subject is that authentication; the application's named schemes still serve endpoints that ask for them explicitly with `[Authorize(AuthenticationSchemes = "…")]`.
- **The header is not a credential.** It carries test data only. An application without the handler stays anonymous, so the test assertions fail. The handler trusts any well-formed header on an in-process request, so register it only where a test user stands in for the application's own authentication; a malformed or oversized value fails authentication and the request stays anonymous.
- **Claim values stay off the trace.** They travel in the header; the `auth:user` entity records the name, roles and claim *types*, never the values, and a gRPC call's `prototest-user` metadata is redacted in the trace.
- **One identity per test.** Declaring it twice replaces, not merges. A test that needs several simultaneous identities keeps using `.Auth(...)` per request or the application's own provisioning.

## Writing your own

The sample suite uses one authenticator per user type:

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

`[Auth<T>(args)]` and `.Auth<T>(args)` construct `T` with `ActivatorUtilities`, so the constructor can mix **positional arguments** from the attribute with **services** from the test's DI scope, including `ProtoExecutionContext` itself:

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

The same authenticator serves GraphQL too: `[Auth<T>]` applies to every HTTP-based protocol the application exposes, and its `Protocols` property narrows it when an application exposes both and you only want one. [Extending ProtoTest](../../advanced/extending.md) covers the factory behind it.
