---
sidebar_position: 1
title: Integrations map
sidebar_label: Map
description: "Every ProtoTest integration on one page: protocols, browsers, messaging, devices, data and files, applications, runners and the tooling around the run."
---

import StackBuilder from '@site/src/components/StackBuilder';
import CapabilityIndex from '@site/src/components/CapabilityIndex';

# Integrations map

Every integration is a NuGet package that joins the same host, execution context and trace, so a suite composes several without learning several models. They all share the [foundation](../foundation/overview.md): one lifecycle, one set of attributes and one report.

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddRest(rest => rest.AddClient("Api")));
```

The groups below are the map. Each line names the packages and links the page that explains the integration, with its deeper pages after it. [Installation](../getting-started/installation.md) starts a project; a [runner](../runners/overview.md) runs the suite.

<StackBuilder />

## Protocols

- **REST** (`ProtoTest.Rest`) - one HTTP client per test on `IHttpClientFactory`, JSON shape assertions, shared authentication, request and response capture, and endpoint coverage. [Overview](./rest/index.md), [requests](./rest/requests.md), [responses](./rest/responses.md), [authentication](./rest/authentication.md), [attachments](./rest/attachments.md).
- **GraphQL** (`ProtoTest.GraphQL`) - queries, mutations and subscriptions over WebSocket or SSE, file uploads, shape assertions and schema coverage. [Overview](./graphql/index.md), [operations](./graphql/operations.md), [responses](./graphql/responses.md), [subscriptions](./graphql/subscriptions.md), [coverage](./graphql/coverage.md).
- **gRPC** (`ProtoTest.Grpc`) - unary and streaming calls, metadata authentication through the shared `[Auth<T>]` pipeline, shape and status assertions, and method coverage. [Overview](./grpc/index.md).
- **OpenAPI** (`ProtoTest.OpenApi`) - compares REST traffic and shape assertions against your OpenAPI document and reports the endpoints, responses and properties no test has checked. [OpenAPI](./openapi.md).

## Browsers

- **Web** (`ProtoTest.Web`, with `ProtoTest.Web.Playwright` or `ProtoTest.Web.Selenium` underneath) - a driver-independent model of pages, components, elements, tables, flows and login, with page coverage and failure artifacts. [Overview](./web/index.md), [page objects](./web/page-objects.md), [locators](./web/locators.md), [interactions](./web/interactions.md), [flows](./web/flows.md), [login](./web/login.md), [middleware](./web/middleware.md), [diagnostics](./web/diagnostics.md).

## Messaging

- **Messaging** (`ProtoTest.Messaging`) - publish to a destination, then await the message that matters with a predicate and a timeout; an in-memory broker by default. [Messaging](./messaging/index.md).
- **RabbitMQ** (`ProtoTest.Messaging.RabbitMq`, `ProtoTest.Messaging.RabbitMq.Testcontainers`) - a real broker adapter, with a broker owned by the run when it should start one. [Messaging](./messaging/index.md).
- **MassTransit** (`ProtoTest.Messaging.MassTransit`) - bridges the messaging surface to an in-process application's MassTransit test harness, and the envelope helper speaks the wire format over any adapter. [MassTransit bridge](./messaging/masstransit.md).

## Devices

- **Devices** (`ProtoTest.Devices`) - typed device clients, one instance per test, frame exchange and device-protocol coverage. [Devices](./devices.md).
- **WebSocket transport** (`ProtoTest.Devices.WebSocket`, `ProtoTest.Devices.WebSocket.AspNetCore`) - `ws://` and `wss://` endpoints, or in-process sockets through the application's test server. [Devices](./devices.md).
- **MQTT transport** (`ProtoTest.Devices.Mqtt`, `ProtoTest.Devices.Mqtt.Testcontainers`) - publish and subscribe against a real broker, with a Mosquitto broker owned by the run. [Devices](./devices.md).

## Data and files

- **Test data** (`ProtoTest.Data`) - deterministic builders with member defaults, factories and provisioners, so a test writes only the values it is about. [Overview](./data/index.md), [defaults](./data/defaults.md), [provisioners](./data/provisioners.md).
- **SQL** (`ProtoTest.Sql`, `ProtoTest.Sql.EntityFrameworkCore`, `ProtoTest.Sql.Testcontainers`) - a database connection each test owns, optional transaction rollback, EF Core contexts on the same connection, and a PostgreSQL server owned by the run. [SQL](./sql/index.md).
- **Sheets** (`ProtoTest.Sheets`) - opens the `.xlsx` your application generated and asserts on cells, ranges, tables and typed rows, with range coverage. [Sheets](./sheets/index.md).

## Applications and runs

- **ASP.NET Core** (`ProtoTest.AspNetCore`) - runs the application in-process with `WebApplicationFactory` and hands its `HttpClient` to the REST and GraphQL clients. [ASP.NET Core](./aspnetcore.md).
- **Background workers** (`ProtoTest.Hosting`) - runs a generic host with its `IHostedService`s inside the test process, the way ASP.NET Core runs an API. [Background workers](./hosting.md).
- **Aspire** (`ProtoTest.Aspire`) - starts an Aspire AppHost with the run and publishes each resource's endpoint as its application's address. [Aspire](./aspire.md).

## Fakes

- **WireMock** (`ProtoTest.WireMock`) - a fake HTTP service per test or per run, with stubs, call verification, trace evidence and stub coverage. [WireMock](./wiremock.md).

## Foundation and support

- **Foundation** (`ProtoTest.Core`) - the host, execution context, hooks, attributes, test ids, tracing and resources every integration plugs into. [Overview](../foundation/overview.md).
- **HTTP plumbing** (`ProtoTest.Http`) - the shared client pipeline and the `[Auth<T>]` model behind REST, GraphQL and gRPC; normally transitive. [Clients](../foundation/clients.md).
- **JSON shapes** (`ProtoTest.Json`) - partial shape matching and `JsonValue` constraints; normally transitive. [Shape matching](../foundation/shape-matching.md).
- **Containers** (`ProtoTest.Testcontainers`) - the run-scoped container base: start once, release once, with `TryStart` for a run that has no container runtime. [Infrastructure](../foundation/infrastructure.md).

## Runners

- **Test runners** (`ProtoTest.NUnit`, `ProtoTest.Xunit`, `ProtoTest.Xunit3`, `ProtoTest.MSTest`, `ProtoTest.TUnit`) - the same runtime in every runner, with one assertion and attachment model. [Overview](../runners/overview.md), [NUnit](../runners/nunit.md), [xUnit v2](../runners/xunit.md), [xUnit v3](../runners/xunit3.md), [MSTest](../runners/mstest.md), [TUnit](../runners/tunit.md).

## Tooling and agent

- **The CLI** (`ProtoTest.Cli`) - the `prototest` tool: `summary`, `index`, `verify` and `feedback` over a trace, a folder of runs or two reports, with no agent in the loop. [Loop](../agent-workflows/loop.md).
- **Traces** (`ProtoTest.Traces`) - the `.prototrace` archive and the reader that opens it: the run's operations, checks and artifacts in one file. [ProtoTrace](../observability/prototrace.md).
- **MCP server** (`ProtoTest.Mcp`) - a local stdio server that answers questions about the runs in a repository through four read-only tools. [Setup](../agent-workflows/setup.md).
- **Diagnosis** (`ProtoTest.Diagnosis`) - the run summary and the failing test's context package, behind the CLI and the MCP tools. [Diagnosis](../agent-workflows/diagnosis.md).
- **Verification** (`ProtoTest.Verification`) - compares two reports and records whether a fix regressed a covered unit or changed the specification. [Verification](../agent-workflows/verification.md).
- **Feedback** (`ProtoTest.Feedback`) - the composite action: installs the CLI, uploads the trace, posts the digest and runs the verdict. [Continuous integration](../continuous-integration/index.md).
- **Analyzers** (`ProtoTest.Analyzers`) - two Roslyn warnings for test code that compiles but runs outside the lifecycle. [Analyzers](../project/analyzers.md).
- **Templates** (`ProtoTest.Templates`) - `dotnet new prototest` scaffolds an API and a suite for it, already composed. [Installation](../getting-started/installation.md).
- **Reporting** (`ProtoTest.Reporting`) - the JSON and HTML report sinks. [Reporting](../observability/reporting.md).
- **OpenTelemetry** - ProtoTest operations are `Activity`s on the `ProtoTest` source; subscribe with `AddSource`. [OpenTelemetry](../observability/opentelemetry.md).

<CapabilityIndex />
