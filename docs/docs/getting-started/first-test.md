---
sidebar_position: 2
title: Your first test
description: "Build a small ProtoTest suite against an ASP.NET Core API, run it, make it fail once, and read the trace."
---

# Your first test

This page goes from an empty test project to a passing test, a failure message and the trace that recorded both. It uses **NUnit**; the other runners differ only in the setup class, covered in [Test runners](../runners/overview.md).

:::tip[Rather start from a working solution?]
`dotnet new install ProtoTest.Templates`, then `dotnet new prototest -n Orders` creates an API and a suite for it that is already composed, traced and reported. Steps 1, 2, 3 and 6 below are ready to run, and `--runner` writes the suite for xUnit v2, xUnit v3, TUnit or MSTest instead. See [Installation](./installation.md#start-from-the-template).
:::

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

One class per test project builds the host. With NUnit it is a `[SetUpFixture]`:

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

This describes one application, `Api`, exposing REST, served from your application running **in-process**: no deployed environment and no port. Point it at a real address in an environment by setting `ProtoTest:Applications:Api:BaseUrl`.

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
    public async Task CreatingAnOrderReturnsIt()
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
- `Proto.Context` is available anywhere in the test: no base class, no injected parameter.

Run it with `dotnet test`. What you should see: one passed test, and a trace file under `TestResults/`.

## 4. Assert on the response

A status code says little. Describe the parts of the body the behaviour depends on:

```csharp
using ProtoTest.Json;

response
    .Should.HaveHttpStatus(HttpStatusCode.Created)
    .Should.MatchShape(new
    {
        id = JsonValue.GreaterThan(0),
        product = "notebook",
        quantity = 2,
        status = "pending"
    });
```

`Should.HaveHttpStatus` returns the response, so the shape assertion chains; `ShouldNot` is the negated form. The shape is **partial**: properties you do not list are ignored, and every mismatch is reported at once with its JSON path. See [Shape matching](../foundation/shape-matching.md).

## 5. Make it fail once

Change `status = "pending"` to `"cancelled"` and run the test again. What you should see: the test fails with the request, the JSON path and both values in the message:

```
GET /api/orders - Shape mismatch failed with 1 error(s):
  • [$.status]: Values did not match. (Expected: "cancelled", Actual: "pending")
```

The message names the fix. Change the expectation back, and the test passes. The [Read the trace](/learn/one-test-one-journey/read-the-trace) lesson walks a real failing trace the same way.

## 6. Read the trace

Tracing is on by default. Without `ConfigureTracing` the trace is written to `TestResults/prototest-{runId}.prototrace` under the test project's output folder. To choose the path yourself, add to `Setup`:

```csharp
protected override void Configure(IProtoHostBuilder builder) =>
    builder
        .ConfigureTracing(trace => trace.OutputPath = "TestResults/orders.prototrace")
        .AddApplication("Api", app => app
            .AddAspNetCoreServer<Program>()
            .AddRest(rest => rest.AddClient("Api")));
```

Run the tests, then:

- drop `TestResults/orders.prototrace` onto [trace.prototest.dev](https://trace.prototest.dev) to see every step of the test, the request and the shape comparison;
- open `report.html` if you added `AddSink<HtmlReportSink>()` from [Reporting](../observability/reporting.md) for the endpoints the suite exercised.

## Going further: turn setup into a capability

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

And an authenticator that uses it:

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
    public async Task CreatingAnOrderReturnsIt()
    {
        // Every request is now authenticated as a fresh customer.
    }
}
```

Using `context.TestId` in the email keeps parallel tests from colliding. Read [Attributes](../foundation/attributes.md) for ordering, teardown and more.

## Limits

- **One context per async flow.** Starting a second test before completing the active one throws. `Proto.Context` outside a test throws and names the alternatives (`ProtoHost.FindTraceWriter(Activity?)` off-flow, `ProtoHost.CurrentHost` for run scope).
- **A skip starts nothing.** A test stopped by a [skip condition](../foundation/skip-conditions.md) has no context, no trace record and no teardown.
- **Names are unique per test.** A name without a prefix is stored as `{testId}-{name}`, and a duplicate attachment name throws.
- **Ids are configurable.** `ConfigureTestIds(ids => ids.RunPrefix = 42)` fixes the run prefix; `SequenceDigits` defaults to `6` and accepts 1 to 9. See [Configuration](./configuration.md).

## Where to next

- [Configuration](./configuration.md): the host options, and how to run the same suite against a deployed environment.
- [Troubleshooting](./troubleshooting.md): when the host, a client or a container does not come up.
- [Foundation](../foundation/overview.md): how the lifecycle, context and attributes fit together.
- [Observability](../observability/coverage.md): what your suite covered, and what it did not.
- [Recipes](../recipes/overview.md): common journeys, ready to adapt.
