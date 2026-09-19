# Changelog

All notable changes to ProtoTest are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-09-19

ProtoTest is a composable integration-testing foundation for .NET 8, 9 and 10. It keeps the test runner you
already use and owns the execution around each test: one host, one execution context and one explicit
lifecycle, with every client, resource, observation and trace attached to that lifecycle. This is the first
stable release; all packages share one version and ship together.

### Added

**Host and lifecycle**

- One `ProtoHost` per test process and one `ProtoExecutionContext` per test, with run hooks and gates, test
  hooks, attributes, typed state, clients, attachments, findings and owned resources.
- Skip conditions (`[RequiresCapability]`, `[RequiresInProcess]`, `[RequiresPlaywrightBrowser]`) that stop a
  test before its lifecycle starts when the host cannot run it.

**Integrations**

- **REST** (`ProtoTest.Rest`): HTTP clients, `[Auth<T>]`, status and shape assertions, REST coverage.
- **GraphQL** (`ProtoTest.GraphQL`): queries, mutations, WebSocket/SSE subscriptions, uploads, schema coverage.
- **gRPC** (`ProtoTest.Grpc`): unary and streaming clients, metadata auth, method coverage.
- **Messaging** (`ProtoTest.Messaging`, `ProtoTest.Messaging.RabbitMq`): publish and await messages on an
  in-memory default broker or RabbitMQ.
- **SQL and EF Core** (`ProtoTest.Sql`, `ProtoTest.Sql.EntityFrameworkCore`): one database connection per test,
  optional transaction isolation, and a `DbContext` over the same connection.
- **Data** (`ProtoTest.Data`): deterministic builders, member defaults, provisioners and the `Ref<T>` identity map.
- **ASP.NET Core in-process** (`ProtoTest.AspNetCore`): host the application inside the test process, with
  server DI access.
- **Browser sessions** (`ProtoTest.Web` with Playwright and Selenium backends): sessions, page objects, flows,
  login and page coverage.
- **Spreadsheets** (`ProtoTest.Sheets`): cell, column, range, table and typed-model assertions for `.xlsx` files.
- **OpenAPI** (`ProtoTest.OpenApi`): contract coverage over REST response and shape observations.
- **Containers** (`ProtoTest.Sql.Testcontainers`, `ProtoTest.Messaging.RabbitMq.Testcontainers`,
  `ProtoTest.Testcontainers`): run-scoped PostgreSQL and RabbitMQ, or your own `ProtoContainerResource<TContainer>`.

**Observability**

- **Coverage** of REST endpoints, OpenAPI documents, GraphQL schemas, gRPC services, web pages and spreadsheet ranges.
- **ProtoTrace** format 2.0: a portable `.prototrace` bundle (`spans.json`, `state.json`) recording what ran and
  what existed and changed, read in the [viewer](https://trace.prototest.dev).
- **Reports** (`ProtoTest.Reporting`): JSON and HTML report sinks.
- **OpenTelemetry bridge** (`ProtoTest.OpenTelemetry`): export ProtoTest operations as OpenTelemetry spans.

**Runners and templates**

- Adapters for **NUnit**, **xUnit v2**, **xUnit v3**, **MSTest** and **TUnit**, sharing one lifecycle and one
  set of outcomes.
- `ProtoTest.Templates`: `dotnet new prototest` creates an API and a suite for it, already composed, traced and
  reported.

[1.0.0]: https://github.com/MSeys/ProtoTest/releases/tag/v1.0.0
