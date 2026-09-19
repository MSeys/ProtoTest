---
sidebar_position: 2
title: Your first test
description: "Build a small ProtoTest suite against an ASP.NET Core API step by step: a request, a shape assertion, a reusable capability, the trace and coverage."
---

# Your first test

This tutorial builds a small suite against an ASP.NET Core API, one step at a time: a first request, a shape assertion, a reusable capability, and finally the trace and coverage report. It uses **NUnit** — the other runners differ only in the setup ceremony, covered in [Test Runners](../runners/overview.md).

## 1. Create the project

:::tip[Rather start from a working solution?]
`dotnet new install ProtoTest.Templates`, then `dotnet new prototest -n Orders` creates an API and a suite for it that is already composed, traced and reported — steps 1 to 4 and 6 of this tutorial, ready to run. See [Installation](./installation.md#quick-start-the-template).
:::

```bash
dotnet new nunit -n Orders.Tests
cd Orders.Tests
dotnet add reference ../Orders.Api/Orders.Api.csproj
dotnet add package ProtoTest.NUnit --prerelease
dotnet add package ProtoTest.Rest --prerelease
dotnet add package ProtoTest.AspNetCore --prerelease
dotnet add package ProtoTest.Reporting --prerelease
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

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
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
using ProtoTest.Json;

response
    .Should.HaveHttpStatus(HttpStatusCode.Created)
    .ShouldMatchShape(new
    {
        id = JsonValue.GreaterThan(0),
        product = "notebook",
        quantity = 2,
        status = "pending"
    });
```

`Should.HaveHttpStatus` returns the response, so the shape assertion chains; `ShouldNot` is the negated form. The shape is **partial** — properties you don't list are ignored — and every mismatch is reported at once with its JSON path. See [Shape matching](../foundation/shape-matching.md).

## 5. Turn setup into a capability

Suppose every order test needs a signed-in customer. Instead of a `[SetUp]` method, write an attribute once:

```csharp
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
            .Should.HaveHttpStatus(HttpStatusCode.Created)
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
[Auth<CustomerAuthenticator>]
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

(`using ProtoTest.Reporting;` for the sink, `using ProtoTest.Rest;` for the collector.)

Run the tests again, then:

- open `TestResults/report.html` for the endpoints your suite exercised;
- drop `TestResults/orders.prototrace` onto [trace.prototest.dev](https://trace.prototest.dev) to see every step of every test — the customer being created, the authenticated request, the shape comparison.

Tracing is on even without `ConfigureTracing`; that call only chooses where the file goes. Without it, the trace is written to `TestResults/prototest-{runId}.prototrace`.

## Rules, options and limits

- **One context per async flow.** Starting a second test before completing the active one throws. `Proto.Context` outside a test throws *"No active ProtoExecutionContext available on this thread."*
- **A skip starts nothing.** A test stopped by a [skip condition](../foundation/skip-conditions.md) has no context, no trace record and no teardown.
- **Ids are configurable.** `ConfigureTestIds(ids => ids.RunPrefix = 42)` fixes the run prefix (random six digits by default); `SequenceDigits` defaults to `6` and accepts 1–9. Ids are at most 18 digits.
- **Tracing is configurable.** `ProtoTraceOptions` also has `Enabled`, `ActivitySources`, `CaptureSourceLocations` and `EmbedSources`; `EmbedSources` only applies when `CaptureSourceLocations` is on. See [Configuration](./configuration.md).
- **Attachment names are namespaced.** A name without a prefix is stored as `{testId}-{name}`, and a duplicate name throws.

## Where to next

- [Configuration](./configuration.md) — the host options and how to run the same suite against a deployed environment.
- [Troubleshooting](./troubleshooting.md) — when the host, a client or a container does not come up.
- [Foundation](../foundation/overview.md) — how the lifecycle, context and attributes fit together.
- [Coverage](../observability/coverage.md) — add `OpenApiCoverageCollector` to find what your suite *doesn't* test.
- Browser tests with [Web](../integrations/web/index.md), GraphQL with [GraphQL](../integrations/graphql/index.md), test data with [Data](../integrations/data/index.md).
