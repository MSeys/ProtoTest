---
sidebar_position: 1
title: Overview
description: "Every ProtoTest integration adds a client to the same execution context, so several in one test share one lifecycle, one trace and one set of attributes."
---

# Integrations overview

Each integration is a NuGet package that adds a client to `ProtoExecutionContext`. They all share the [foundation](../foundation/overview.md) — lifecycle, attributes, attachments, tracing, coverage — so using several in one test doesn't mean learning several models. Every library and runner package targets .NET 8, 9 and 10 and ships as a prerelease, so `dotnet add package` lines use `--prerelease`.

## Quick start

Compose one application and give it a protocol:

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddRest(rest => rest.AddClient("Api")));
```

```csharp
[Application("Api")]
public sealed class ApiTests
{
    [ProtoTest]
    public async Task Orders_are_created()
    {
        using var response = await Proto.Context.Rest()
            .Body(new { product = "notebook", quantity = 2 })
            .PostAsync("/api/orders");

        response.Should.HaveHttpStatus(HttpStatusCode.Created)
            .ShouldMatchShape(new { id = JsonValue.GreaterThan(0), product = "notebook" });
    }
}
```

Run this with any [runner](../runners/overview.md), then add `AddGraphQL`, `AddWeb`, `AddMessaging`, … to the same application: the same context hands each protocol its client, and everything lands in one trace and one report.

## Going further: several integrations in one test

Adapted from `samples/ProtoTest.Demo/SheetsJourney.cs` — data through one package, an HTTP download through another, and a spreadsheet assertion through a third, all in one test:

```csharp
[ProtoTest]
[SignedInAs]
public async Task TheMonthlyReport_ShouldMatchItsModel()
{
    var project = await Proto.Context.Data()
        .For<CreateProjectRequest>()
        .With(request => request.Name, "report-atlas")
        .CreateAsync<ProjectResponse>();

    using var response = await Proto.Context.Rest().GetAsync("/api/v1/reports/monthly.xlsx");
    response.Should.HaveHttpStatus(HttpStatusCode.OK);
    var report = Proto.Context.Sheets().Open(response).Model<ProjectReportRow>();

    report.Verify();
    report.Column(row => row.Environments).ShouldAll(count => count >= 0);
}
```

The same pattern works across protocols: change something through REST and check it through GraphQL in the same test, with one `[Auth<T>]` authenticator serving both. The [recipes](../recipes/overview.md) walk through scenarios like these end to end.

## The package matrix

### Integration packages

| Package | Install | What it adds | Reach it with |
| --- | --- | --- | --- |
| `ProtoTest.Rest` | `dotnet add package ProtoTest.Rest --prerelease` | HTTP/REST client, status and shape assertions, `[Auth<T>]`, REST coverage | `Proto.Context.Rest()` |
| `ProtoTest.GraphQL` | `dotnet add package ProtoTest.GraphQL --prerelease` | queries, mutations, WebSocket/SSE subscriptions, uploads, schema coverage | `Proto.Context.GraphQL()` |
| `ProtoTest.Grpc` | `dotnet add package ProtoTest.Grpc --prerelease` | unary and streaming gRPC clients, metadata auth, shape/status assertions, method coverage | `Proto.Context.Grpc()` |
| `ProtoTest.Messaging` | `dotnet add package ProtoTest.Messaging --prerelease` | publish and await messages, with an in-memory default broker | `Proto.Context.Messaging()` |
| `ProtoTest.Messaging.RabbitMq` | `dotnet add package ProtoTest.Messaging.RabbitMq --prerelease` | RabbitMQ adapter for the messaging client | `messaging.UseRabbitMq()` |
| `ProtoTest.Sheets` | `dotnet add package ProtoTest.Sheets --prerelease` | `.xlsx` cell, column, range, table and typed-model assertions, range coverage | `Proto.Context.Sheets()` |
| `ProtoTest.Data` | `dotnet add package ProtoTest.Data --prerelease` | deterministic data, member defaults, provisioners and the `Ref<T>` identity map | `Proto.Context.Data()` |
| `ProtoTest.Sql` | `dotnet add package ProtoTest.Sql --prerelease` | one database connection per test, optional transaction isolation | `Proto.Context.SqlConnection()` |
| `ProtoTest.Sql.EntityFrameworkCore` | `dotnet add package ProtoTest.Sql.EntityFrameworkCore --prerelease` | EF Core context over the per-test connection, enlisted in its transaction | `Proto.Context.Sql<TContext>()` |
| `ProtoTest.Web` | `dotnet add package ProtoTest.Web --prerelease` | backend-neutral sessions, page objects, flows, login and page coverage | `Proto.Context.Web()` |
| `ProtoTest.Web.Playwright` | `dotnet add package ProtoTest.Web.Playwright --prerelease` | Playwright backend, browser pool and `[RequiresPlaywrightBrowser]` probe | `builder.AddWeb()` |
| `ProtoTest.Web.Selenium` | `dotnet add package ProtoTest.Web.Selenium --prerelease` | Selenium backend with its own actionability loop | `builder.AddWeb(createDriver)` |
| `ProtoTest.AspNetCore` | `dotnet add package ProtoTest.AspNetCore --prerelease` | in-process ASP.NET Core application, server DI access, page inventory | `Proto.Context.ServerFactory<TProgram>()` |
| `ProtoTest.OpenApi` | `dotnet add package ProtoTest.OpenApi --prerelease` | OpenAPI contract coverage over REST response and shape observations | `.AddCollector<OpenApiCoverageCollector>()` |
| `ProtoTest.Sql.Testcontainers` | `dotnet add package ProtoTest.Sql.Testcontainers --prerelease` | a PostgreSQL container owned by the run | `builder.AddInfrastructure(PostgresDatabase.Container(), "ConnectionStrings:Northstar")` |
| `ProtoTest.Messaging.RabbitMq.Testcontainers` | `dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers --prerelease` | a RabbitMQ container owned by the run | `builder.AddInfrastructure(RabbitMqBroker.Container(), RabbitMqOptions.ConnectionStringSetting, …)` |

### Foundation and supporting packages

| Package | Install | What it adds | Reach it with |
| --- | --- | --- | --- |
| `ProtoTest.Core` | `dotnet add package ProtoTest.Core --prerelease` | host, execution context, hooks, attributes, test ids, tracing, run gates and resources | `Proto.Context`, `Proto.Host` |
| `ProtoTest.Http` | `dotnet add package ProtoTest.Http --prerelease` | shared HTTP client plumbing and the `[Auth<T>]` model behind REST, GraphQL and gRPC; an extension point, normally transitive | `Proto.Context.Client<HttpClient>(name)` |
| `ProtoTest.Json` | `dotnet add package ProtoTest.Json --prerelease` | partial JSON shape matching and `JsonValue` constraints; normally transitive | `JsonShapeMatcher.AssertMatch`, `JsonValue` |
| `ProtoTest.Testcontainers` | `dotnet add package ProtoTest.Testcontainers --prerelease` | `ProtoContainerResource<TContainer>` base for run-scoped containers: start-once, release-once, `TryStart` | `builder.AddInfrastructure(…)` |
| `ProtoTest.Reporting` | `dotnet add package ProtoTest.Reporting --prerelease` | JSON and HTML report sinks | `builder.AddSink<JsonReportSink>()`, `AddSink<HtmlReportSink>()` |
| `ProtoTest.OpenTelemetry` | `dotnet add package ProtoTest.OpenTelemetry --prerelease` | export ProtoTest operations as OpenTelemetry spans | `tracerBuilder.AddProtoTestInstrumentation()` |

### Runners and templates

| Package | Install | What it adds | Reach it with |
| --- | --- | --- | --- |
| `ProtoTest.NUnit` | `dotnet add package ProtoTest.NUnit --prerelease` | NUnit lifecycle adapter and attachment publisher | `[ProtoTest]`, `ProtoTestAssembly` |
| `ProtoTest.Xunit` | `dotnet add package ProtoTest.Xunit --prerelease` | xUnit v2 adapter (one collection for the suite) | `[ProtoTestFact]`, `[ProtoTestTheory]` |
| `ProtoTest.Xunit3` | `dotnet add package ProtoTest.Xunit3 --prerelease` | xUnit v3 adapter | `[ProtoTestFact]`, `[ProtoTestTheory]`, assembly fixture |
| `ProtoTest.MSTest` | `dotnet add package ProtoTest.MSTest --prerelease` | MSTest lifecycle adapter and attachment publisher | `[ProtoTest]` |
| `ProtoTest.TUnit` | `dotnet add package ProtoTest.TUnit --prerelease` | TUnit executor and attachment publisher | `[assembly: TestExecutor<ProtoTestExecutor>()]` |
| `ProtoTest.Templates` | `dotnet new install ProtoTest.Templates` | the `prototest` starter solution: an API and a suite for it, already composed | `dotnet new prototest -n Shop` |

## Container-backed dependencies

When a suite should run against a real server instead of an in-memory one, a container package owns it for the run: `ProtoTest.Sql.Testcontainers` starts PostgreSQL, and `ProtoTest.Messaging.RabbitMq.Testcontainers` starts RabbitMQ. Register the container with `AddInfrastructure(...)` so the host starts it before the run, fills its connection string into configuration for the application and the tests, and releases it after the reports are written. Both build on `ProtoTest.Testcontainers`, whose `TryStart` lets a fixture fall back when no container runtime is available — see [Infrastructure](../foundation/infrastructure.md).

The same suite can then run in-process, container-backed or against a published environment without a code change; [Environments](../getting-started/environments.md) explains what changes in each, including why environment-specific journeys skip rather than fail.
