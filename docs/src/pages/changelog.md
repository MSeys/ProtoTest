---
title: Changelog
description: What changed in each ProtoTest release, with every breaking change and how to update for it.
---

# Changelog

Every ProtoTest package shares one version number, so this page lists releases, not packages. While ProtoTest is in preview, a release can change public APIs; each one that does lists the change under **Breaking** with what to write instead.

## 0.1.0-alpha — not yet released

The first public preview.

### Foundation

- One host per test assembly, one `ProtoExecutionContext` per test, with hooks, attributes, clients and attachments on a single lifecycle.
- Adapters for **NUnit**, **xUnit v2**, **xUnit v3**, **MSTest** and **TUnit**.
- Shape matching for JSON, shared by REST, GraphQL, gRPC and Sheets.
- Run-scoped infrastructure: containers that start with the run, fill configuration for the tests and the application, and are released with it.
- Skip conditions that answer what the host can actually do, before a test's lifecycle starts.

### Integrations

- **ASP.NET Core** in-process hosting, **REST**, **GraphQL** (queries, mutations, subscriptions, uploads), **gRPC** (unary and streaming).
- **Web** with Playwright and Selenium backends: pages, components, flows, login, waits and diagnostics.
- **Data** builders and provisioners, **SQL** with a per-test connection and Entity Framework Core, **Messaging** with a RabbitMQ adapter, and **Sheets** for generated workbooks.

### Observability

- **ProtoTrace** format 2.0: what ran (`spans.json`) and what existed and changed (`state.json`), read in the [viewer](https://trace.prototest.dev).
- Coverage of REST endpoints, OpenAPI documents, GraphQL schemas and gRPC services; JSON and HTML reports; an OpenTelemetry bridge.

### Getting started

- `ProtoTest.Templates`: `dotnet new prototest` creates an API and a suite that is composed, traced and reported.
