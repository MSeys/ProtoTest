---
sidebar_position: 2
title: Your first test
---

# Your first test

This tutorial builds a small suite against an ASP.NET Core API, one step at a time: a first request, a shape assertion, a reusable capability, and finally the trace and coverage report. It uses **NUnit** — the other runners differ only in the setup ceremony, covered in [Test Runners](../runners/overview.md).

## 1. Create the project

```bash
dotnet new nunit -n Orders.Tests
cd Orders.Tests
dotnet add reference ../Orders.Api/Orders.Api.csproj
dotnet add package ProtoTest.NUnit
dotnet add package ProtoTest.Rest
dotnet add package ProtoTest.AspNetCore
dotnet add package ProtoTest.Reporting
```

For a minimal-API application, make its entry point visible to the tests by adding this to `Orders.Api`:

```csharp
public partial class Program;
```

## 2. Configure the host

One class per test project builds the host. With NUnit it's a `[SetUpFixture]`:

```csharp
using NUnit.Framework;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

namespace Orders.Tests;

[SetUpFixture]
public sealed class Setup : ProtoTestAssembly
{
    protected override void Configure(IProtoHostBuilder builder) =>
        builder.AddApplication("Api", app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest.AddClient("Api")));
}
```

This describes one application, `Api`, exposing REST, served from your application running **in-process** — no deployed environment and no port. Point it at a real address in an environment by setting `ProtoTest:Applications:Api:BaseUrl`.

:::tip
A `[SetUpFixture]` only covers its own namespace and the namespaces below it. Keep your tests in or under `Orders.Tests`.
:::

## 3. Write a test

```csharp
using System.Net;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.NUnit;
using ProtoTest.Rest;

namespace Orders.Tests;

[Application("Api")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task Creating_an_order_returns_it()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 2 })
            .PostAsync("/api/orders");

        response.ShouldHaveHttpStatus(HttpStatusCode.Created);
    }
}
```

- `[ProtoTest]` replaces NUnit's `[Test]` and wraps the test in a ProtoTest context.
- `[Application("Api")]` selects the `Api` application; `Proto.Context.Rest()` then uses its default REST client.
- `Proto.Context` is available anywhere in the test — no base class, no injected parameter.

Run it with `dotnet test`.

## 4. Assert on the response

A status code says little. Describe the parts of the body the behaviour depends on:

```csharp
response
    .ShouldHaveHttpStatus(HttpStatusCode.Created)
    .ShouldMatchShape(new
    {
        id = JsonValue.GreaterThan(0),
        product = "notebook",
        quantity = 2,
        status = "pending"
    });
```

Add `using ProtoTest.Json;` for `JsonValue`. The shape is **partial** — properties you don't list are ignored — and every mismatch is reported at once with its JSON path. See [Shape matching](../advanced/json-shapes.md).

## 5. Turn setup into a capability

Suppose every order test needs a signed-in customer. Instead of a `[SetUp]` method, write an attribute once:

```csharp
using System.Net;
using ProtoTest.Core;
using ProtoTest.Rest;

namespace Orders.Tests;

public sealed record CustomerContext(string Id, string AccessToken) : IProtoContext;

public sealed class CustomerAttribute : ProtoAttribute
{
    public override async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        using var response = await context.Rest("Api")
            .WithoutAuth()
            .Body(new { email = $"customer-{context.TestId}@example.test" })
            .PostAsync("/test-support/customers");

        var customer = response
            .ShouldHaveHttpStatus(HttpStatusCode.Created)
            .ReadAsJson<CustomerContext>()!;

        context.SetContext(customer);
    }
}
```

…and an authenticator that uses it:

```csharp
using System.Net.Http.Headers;
using ProtoTest.Http;

namespace Orders.Tests;

public sealed class CustomerAuthenticator : IProtoHttpAuthenticator
{
    public ValueTask AuthenticateAsync(ProtoHttpAuthenticationContext context, CancellationToken cancellationToken = default)
    {
        var customer = context.Test.Resolve<CustomerContext>();
        context.Request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        return ValueTask.CompletedTask;
    }
}
```

Now compose them:

```csharp
[Application("Api")]
[Customer]
[RestAuth<CustomerAuthenticator>]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task Creating_an_order_returns_it()
    {
        // Every request is now authenticated as a fresh customer.
    }
}
```

Using `context.TestId` in the email keeps parallel tests from colliding. Read [Attributes](../foundation/attributes.md) for ordering, teardown and more.

## 6. See what happened

Add a trace path and a report to `Setup`, plus coverage:

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder
        .ConfigureTracing(trace => trace.OutputPath = "TestResults/orders.prototrace")
        .AddApplication("Api", app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest
                .AddClient("Api")
                .AddCollector<RestCoverageCollector>()))
        .AddSink<HtmlReportSink>(sink => sink.OutputPath = "TestResults/report.html");
```

(`using ProtoTest.Reporting;` for the sink.)

Run the tests again, then:

- open `TestResults/report.html` for the endpoints your suite exercised;
- drop `TestResults/orders.prototrace` onto [trace.prototest.dev](https://trace.prototest.dev) to see every step of every test — the customer being created, the authenticated request, the shape comparison.

Tracing is on even without `ConfigureTracing`; the setting only chooses where the file goes.

## Where to next

- [Configuration](./configuration.md) — run the same suite against a deployed environment.
- [Foundation](../foundation/overview.md) — how the lifecycle, context and attributes fit together.
- [Coverage](../advanced/coverage.md) — add `OpenApiCoverageCollector` to find what your suite *doesn't* test.
- Browser tests with [Web](../integrations/web/index.md), GraphQL with [GraphQL](../integrations/graphql/index.md), test data with [Data](../integrations/data/index.md).
