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
| [`ProtoTest.Web`](./web/index.md) + `.Playwright` / `.Selenium` | page objects, flows, login, browser diagnostics | `Proto.Context.Web()` |
| [`ProtoTest.Data`](./data/index.md) | deterministic test data and provisioning | `Proto.Context.Data()` |
| [`ProtoTest.AspNetCore`](./aspnetcore.md) | in-process ASP.NET Core application | `Proto.Context.Server<TProgram>()` |
| [`ProtoTest.OpenApi`](./openapi.md) | OpenAPI contract coverage | a collector on a REST client |

Supporting packages come along automatically when you install one of the above:

| Package | |
| --- | --- |
| `ProtoTest.Core` | the foundation itself |
| `ProtoTest.Http` | shared HTTP clients and the authentication model used by REST and GraphQL |
| `ProtoTest.Json` | shape matching and `JsonValue` constraints |

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

    var administrator = Proto.Context.Context<SampleUserContext>();

    using var response = await Proto.Context.Rest().GetAsync("/api/admin/users");
    response.ShouldHaveStatus(HttpStatusCode.OK).ShouldMatchShape(new
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

## On the roadmap

Not available yet:

- **gRPC** — a `ProtoTest.Grpc` project exists in the repository as a placeholder; it has no implementation and isn't published.
- **Files and documents** — starting with spreadsheets, such as Excel workbooks generated with SpreadsheetGear.
- **Messaging** — asserting on messages published to and consumed from brokers such as RabbitMQ.
- **Infrastructure** — starting real dependencies for a run with Testcontainers.
