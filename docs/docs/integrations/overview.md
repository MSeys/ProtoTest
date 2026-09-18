---
sidebar_position: 1
title: Overview
---

# Integrations overview

Each integration is a NuGet package that adds a client to `ProtoExecutionContext`. They all share the same [foundation](../foundation/overview.md) — lifecycle, attributes, attachments, tracing, coverage — so using several in one test doesn't mean learning several models.

| Package | Adds | Reach it with |
| --- | --- | --- |
| [`ProtoTest.Rest`](./rest/index.md) | HTTP/REST client, shape assertions, authentication | `Proto.Context.Rest()` |
| [`ProtoTest.GraphQL`](./graphql/index.md) | queries, mutations, subscriptions, uploads, schema coverage | `Proto.Context.GraphQL()` |
| [`ProtoTest.Grpc`](./grpc/index.md) | unary and streaming gRPC clients, metadata authentication, service/method coverage | `Proto.Context.Grpc()` |
| [`ProtoTest.Web`](./web/index.md) + `.Playwright` / `.Selenium` | page objects, flows, login, browser diagnostics | `Proto.Context.Web()` |
| [`ProtoTest.Messaging`](./messaging/index.md) | publish messages and await the one that matters | `Proto.Context.Messages()` |
| [`ProtoTest.Sheets`](./sheets/index.md) | spreadsheet assertions and range coverage | `Proto.Context.Sheets()` |
| [`ProtoTest.Data`](./data/index.md) | deterministic test data and provisioning | `Proto.Context.Data()` |
| [`ProtoTest.Sql`](./sql/index.md) | one database connection per test, rolled back at the end | `Proto.Context.SqlConnection()` |
| [`ProtoTest.Sql.EntityFrameworkCore`](./sql/index.md) | Entity Framework Core over the per-test connection | `Proto.Context.Sql<TContext>()` |
| [`ProtoTest.Sql.Testcontainers`](./sql/index.md) | a PostgreSQL container owned by the run | `builder.AddInfrastructure(...)` |
| [`ProtoTest.AspNetCore`](./aspnetcore.md) | in-process ASP.NET Core application | `Proto.Context.ServerFactory<TProgram>()` |
| [`ProtoTest.OpenApi`](./openapi.md) | OpenAPI contract coverage | a collector on a REST client |

Supporting packages come along automatically when you install one of the above:

| Package | |
| --- | --- |
| `ProtoTest.Core` | the foundation itself |
| `ProtoTest.Http` | shared HTTP clients and the authentication model used by REST, GraphQL and gRPC |
| `ProtoTest.Json` | shape matching and `JsonValue` constraints |
| `ProtoTest.Testcontainers` | shared plumbing for run-scoped containers |

And two that you add when you want them:

| Package | |
| --- | --- |
| [`ProtoTest.Reporting`](../advanced/reporting.md) | JSON and HTML report sinks |
| [`ProtoTest.OpenTelemetry`](../advanced/opentelemetry.md) | export ProtoTest operations to OpenTelemetry |

## Mixing integrations in one test

Because every client hangs off the same context, a single test can cross layers. From the sample suite — create data through a provisioner, then verify it through the API:

```csharp
[ProtoTest]
[SampleUser(SampleRoles.TenantAdministrator)]
public async Task AdministratorCanProvisionAndListSevenAdditionalUsers()
{
    var createdUsers = await Proto.Context.Data()
        .For<CreateUserRequest>()
        .With(request => request.Role, SampleRoles.Member)
        .CreateManyAsync<UserResponse>(7);

    var administrator = Proto.Context.Resolve<SampleUserContext>();

    using var response = await Proto.Context.Rest().GetAsync("/api/admin/users");
    response.ShouldHaveHttpStatus(HttpStatusCode.OK).ShouldMatchShape(new
    {
        tenant = administrator.Tenant,
        users = createdUsers
            .Select(user => new { user.Id, user.Email, user.Role })
            .Append(new { administrator.Id, administrator.Email, administrator.Role })
            .OrderBy(user => user.Id, StringComparer.Ordinal)
            .ToArray()
    });
}
```

Or change something through REST and check it's visible through GraphQL, in the same test, with one authenticator serving both.

## Container-backed dependencies

When a suite should run against a real server instead of an in-memory one, a container package owns it for the run: [`ProtoTest.Sql.Testcontainers`](./sql/index.md) starts PostgreSQL, and `ProtoTest.Messaging.RabbitMq.Testcontainers` starts RabbitMQ. Register the container with `AddInfrastructure(...)` so the host starts it before the run, fills its connection string into configuration for the application and the tests, and releases it after the reports are written. Both build on `ProtoTest.Testcontainers`.
