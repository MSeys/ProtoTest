---
title: Changelog
description: What changed in each ProtoTest release, with every breaking change and how to update for it.
---

# Changelog

Every ProtoTest package shares one version number, so this page lists releases, not packages. Each release that changes a public API lists the change under **Breaking** with what to write instead.

## 1.0.0 — 2026-09-19

The first stable release. All packages ship together at 1.0.0:

- **Host and lifecycle** — `ProtoHost`, `ProtoExecutionContext`, run and test hooks, attributes, typed state, clients, resources, skip conditions and run gates.
- **Integrations** — REST, GraphQL (queries, mutations, subscriptions, uploads), gRPC (unary and streaming), Messaging with RabbitMQ, SQL and Entity Framework Core, Data, ASP.NET Core in-process, Web with Playwright and Selenium, Sheets, OpenAPI and run-scoped containers.
- **Observability** — ProtoTrace format 2.0 (`spans.json`, `state.json`) read in the [viewer](https://trace.prototest.dev), coverage of REST, OpenAPI, GraphQL, gRPC, web and spreadsheet surfaces, JSON and HTML reports, and the OpenTelemetry bridge.
- **Runners and templates** — NUnit, xUnit v2, xUnit v3, MSTest and TUnit adapters, and `ProtoTest.Templates` for `dotnet new prototest`.
