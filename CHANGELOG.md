# Changelog

All notable changes to ProtoTest are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.1] - 2026-09-20

### Fixed

- Preserve binary REST response artifacts byte-for-byte instead of converting them through text. This
  keeps downloaded workbooks, PDFs, archives, images and other binary responses valid inside
  `.prototrace` files.
- Correct API documentation links emitted from XML comments across the HTTP, Sheets and Web packages.

### Added

- Preview `.xlsx` artifacts directly in the ProtoTrace viewer, with worksheet tabs, dimensions, sticky
  row and column headers, typed cell values and bounded rendering for large workbooks.
- Add focused, shareable recipe traces for REST-to-GraphQL, REST-to-database and workbook journeys.
- Publish a generated .NET API reference alongside the task-oriented documentation, with one command
  producing the complete uploadable site.
- Add documentation quality, link and API-reference workflows; contributor, support, security and
  code-of-conduct guidance; richer CI guidance; and interactive stack and trace examples.

### Changed

- Improve the documentation landing page, navigation, SEO metadata, social preview and per-page feedback.
- Harden release validation: every ProtoTest package must share one version, every symbols package must
  match its DLL paths and portable-PDB identities, and a release tag must match the package version.
- Refuse duplicate NuGet versions during publishing so a rebuilt symbols package cannot be paired with
  an already immutable DLL from another commit.

## [1.0.0] - 2026-09-19

**ProtoTest 1.0 is here.** What began as a stubborn idea — that an integration test should read like the
scenario it describes while the framework quietly owns everything around it — is now a stable foundation
for .NET 8, 9 and 10. One host, one execution context and one explicit lifecycle; the test runner you
already use; and every integration sharing the same assertions, evidence and coverage, all the way down to
a portable trace you can open and read. Every package ships together at 1.0.0, documented, tested, and
ready for production suites.

ProtoTest is a composable integration-testing foundation: compose capabilities onto a host, and each test
runs through a recorded lifecycle of phases, operations, state changes, checks and findings.

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

**Welcome to 1.0.** Install a package, compose the capabilities your system actually has, and run the same
suite in-process, in containers, or against a published environment. The trace will tell you the rest.

[1.0.1]: https://github.com/MSeys/ProtoTest/releases/tag/v1.0.1
[1.0.0]: https://github.com/MSeys/ProtoTest/releases/tag/v1.0.0
