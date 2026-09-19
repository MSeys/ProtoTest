<p align="center">
  <img width="1200" alt="ProtoTest" src="assets/brand/prototest-banner.svg" />
</p>

**ProtoTest** is a composable integration-testing foundation for .NET 8, 9 and 10. Keep the test runner you already
use — NUnit, xUnit v2, xUnit v3, MSTest or TUnit — and let ProtoTest own the execution around each test: one host,
one execution context, one lifecycle, one trace. REST, GraphQL, gRPC, messaging, SQL, browsers, spreadsheets and
in-process ASP.NET Core all attach to that context, so a test describes behaviour and the infrastructure is written once.

## Quick start

### From the template

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop
cd Shop
dotnet test
```

The template creates a small ASP.NET Core API and a suite for it that is already composed, traced and reported.
The run leaves `TestResults/Shop.prototrace` and `TestResults/Shop.html` in the test project's output folder.

### Into your own project

Add `ProtoTest.Core`, one runner package and one package per integration you use:

```bash
dotnet add package ProtoTest.Core
dotnet add package ProtoTest.NUnit
dotnet add package ProtoTest.Rest
dotnet add package ProtoTest.AspNetCore
dotnet add package ProtoTest.Reporting
```

Full instructions: [Installation](https://prototest.dev/docs/getting-started/installation).

## Packages

Every package is stable on NuGet and installs the same way: `dotnet add package <id>`.

### Capabilities

| Package | What it adds | Install id |
| --- | --- | --- |
| `ProtoTest.Rest` | HTTP/REST client, `[Auth<T>]`, status and shape assertions, REST coverage | `ProtoTest.Rest` |
| `ProtoTest.GraphQL` | queries, mutations, subscriptions, uploads, schema coverage | `ProtoTest.GraphQL` |
| `ProtoTest.Grpc` | unary and streaming clients, metadata auth, method coverage | `ProtoTest.Grpc` |
| `ProtoTest.Messaging` | publish and await messages, with an in-memory default broker | `ProtoTest.Messaging` |
| `ProtoTest.Messaging.RabbitMq` | RabbitMQ adapter for the messaging client | `ProtoTest.Messaging.RabbitMq` |
| `ProtoTest.Sql` | one database connection per test, optional transaction isolation | `ProtoTest.Sql` |
| `ProtoTest.Sql.EntityFrameworkCore` | EF Core context over the per-test connection | `ProtoTest.Sql.EntityFrameworkCore` |
| `ProtoTest.Data` | deterministic data, provisioners and the `Ref<T>` identity map | `ProtoTest.Data` |
| `ProtoTest.AspNetCore` | in-process ASP.NET Core application and server DI access | `ProtoTest.AspNetCore` |
| `ProtoTest.Web` | browser sessions, page objects, flows and page coverage | `ProtoTest.Web` |
| `ProtoTest.Web.Playwright` | Playwright backend and browser pool | `ProtoTest.Web.Playwright` |
| `ProtoTest.Web.Selenium` | Selenium backend | `ProtoTest.Web.Selenium` |
| `ProtoTest.Sheets` | `.xlsx` cell, column, range, table and typed-model assertions | `ProtoTest.Sheets` |
| `ProtoTest.OpenApi` | OpenAPI contract coverage over REST observations | `ProtoTest.OpenApi` |

### Foundation and observability

| Package | What it adds | Install id |
| --- | --- | --- |
| `ProtoTest.Core` | host, execution context, lifecycle, hooks, attributes, resources and trace | `ProtoTest.Core` |
| `ProtoTest.Http` | shared HTTP client and `[Auth<T>]` plumbing behind REST, GraphQL and gRPC | `ProtoTest.Http` |
| `ProtoTest.Json` | partial JSON shape matching and `JsonValue` constraints | `ProtoTest.Json` |
| `ProtoTest.Testcontainers` | run-scoped container base class | `ProtoTest.Testcontainers` |
| `ProtoTest.Sql.Testcontainers` | a PostgreSQL container owned by the run | `ProtoTest.Sql.Testcontainers` |
| `ProtoTest.Messaging.RabbitMq.Testcontainers` | a RabbitMQ container owned by the run | `ProtoTest.Messaging.RabbitMq.Testcontainers` |
| `ProtoTest.Reporting` | JSON and HTML report sinks | `ProtoTest.Reporting` |
| `ProtoTest.OpenTelemetry` | export ProtoTest operations as OpenTelemetry spans | `ProtoTest.OpenTelemetry` |
| `ProtoTest.Templates` | the `prototest` starter solution | `ProtoTest.Templates` |

## Architecture

```text
test runners      NUnit · xUnit v2 · xUnit v3 · MSTest · TUnit
                        │  wraps every test in the same lifecycle
host              ProtoHost — one per process: DI, run hooks, gates, infrastructure
                        │
execution         ProtoExecutionContext — one per test
                        │
     ┌──────────────────┼───────────────────┐
capabilities       foundations        observability
REST · GraphQL     Http · Json        ProtoTrace · coverage
gRPC · Messaging   Testcontainers     JSON/HTML reports
Sql · Data                            OpenTelemetry
Web · Sheets
AspNetCore · OpenAPI
```

A test is Setup → Execution → Rollback → Teardown: clients, state, resources, attachments and findings live on
the execution context, while run-scoped infrastructure starts with the run and is released after the reports.

## Test frameworks

| Runner | Package | Test attribute |
| --- | --- | --- |
| NUnit | `ProtoTest.NUnit` | `[ProtoTest]` |
| xUnit v2 | `ProtoTest.Xunit` | `[ProtoTestFact]`, `[ProtoTestTheory]` |
| xUnit v3 | `ProtoTest.Xunit3` | `[ProtoTestFact]`, `[ProtoTestTheory]` |
| MSTest | `ProtoTest.MSTest` | `[ProtoTest]` |
| TUnit | `ProtoTest.TUnit` | `[Test]` with the ProtoTest executor |

Every adapter runs the same lifecycle and reports the same outcomes, attachments and skip reasons to the runner's
own output.

## Documentation and demo

- [Documentation](https://prototest.dev/docs/) — installation, first test, recipes, foundation, integrations,
  observability and extending.
- [Demo](samples/ProtoTest.Demo) — twelve journeys against Northstar, a multi-tenant SaaS sample app, through REST,
  GraphQL, gRPC, RabbitMQ, spreadsheets and a real browser, with coverage, reports and a full ProtoTrace. The
  browser journeys drive Northstar Console, the application's real Vue 3 SPA, end to end.
- [Trace viewer](https://trace.prototest.dev) — open a `.prototrace` bundle.

## License

[MIT](LICENSE) © 2026 Matthias Seys.
