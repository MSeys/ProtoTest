---
title: Changelog
description: What changed in each ProtoTest release, with every breaking change and how to update for it.
---

# Changelog

Every ProtoTest package shares one version number, so this page lists releases, not packages. Each release that changes a public API lists the change under **Breaking** with what to write instead.

## 1.0.0 — 2026-09-19

**ProtoTest 1.0 is here.** What began as a stubborn idea — that an integration test should read like the scenario it describes while the framework quietly owns everything around it — is now a stable foundation for .NET 8, 9 and 10. One host, one execution context and one explicit lifecycle; the test runner you already use; and every integration sharing the same assertions, evidence and coverage, all the way down to a portable trace you can open and read. Every package ships together at 1.0.0, documented, tested, and ready for production suites. Thank you to everyone who pushed on the preview builds, reported the sharp edges and helped make this release worth the name.

ProtoTest is a composable integration-testing foundation: compose capabilities onto a host, and each test runs through a recorded lifecycle of phases, operations, state changes, checks and findings.

- **Host and lifecycle** — `ProtoHost`, `ProtoExecutionContext`, run and test hooks, attributes, typed state, clients, resources, skip conditions and run gates.
- **Integrations** — REST, GraphQL (queries, mutations, subscriptions, uploads), gRPC (unary and streaming), Messaging with RabbitMQ, SQL and Entity Framework Core, Data, ASP.NET Core in-process, Web with Playwright and Selenium, Sheets, OpenAPI and run-scoped containers.
- **Observability** — ProtoTrace format 2.0 (`spans.json`, `state.json`) read in the [viewer](https://trace.prototest.dev), coverage of REST, OpenAPI, GraphQL, gRPC, web and spreadsheet surfaces, JSON and HTML reports, and the OpenTelemetry bridge.
- **Runners and templates** — NUnit, xUnit v2, xUnit v3, MSTest and TUnit adapters, and `ProtoTest.Templates` for `dotnet new prototest`.

**Welcome to 1.0.** Install a package, compose the capabilities your system actually has, and run the same suite in-process, in containers, or against a published environment. The trace will tell you the rest.
