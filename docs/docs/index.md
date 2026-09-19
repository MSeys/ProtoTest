---
id: index
slug: /
sidebar_position: 0
title: Introduction
description: "ProtoTest is a composable integration-testing foundation for .NET: REST, GraphQL, gRPC, browsers, data, SQL and messaging on one host, one context and one lifecycle."
---

# ProtoTest

ProtoTest is a composable integration-testing foundation for .NET 8, 9 and 10. REST, GraphQL, browser automation, test data and in-process ASP.NET Core all attach to **one host, one execution context and one lifecycle** — so a test describes behaviour, and the infrastructure around it is written once and reused.

Packages ship as prereleases (`0.1.0-alpha`), so installs use `--prerelease`; the template targets `net10.0` unless `--framework net8.0` or `net9.0` is passed.

:::caution[Work in progress]
ProtoTest and this documentation are under active development. APIs may still change before a first stable release, and some pages are still being written. If something looks wrong or missing, [open an issue](https://github.com/MSeys/ProtoTest/issues).
:::

## What it gives you

- **Capabilities as attributes.** "A fresh tenant", "a billing administrator", "logged into the portal" become attributes you write once and compose onto any test.
- **Clients that share a context.** `Proto.Context.Rest()`, `.GraphQL()`, `.Web()` and `.Data()` all live on the same per-test context, so one test can create data, drive the browser and verify through the API.
- **Assertions that describe shapes.** Say what the JSON should look like — partially, with constraints — and get every mismatch at once.
- **Contract coverage.** Find the endpoints, status codes, response fields and GraphQL fields your suite never checked.
- **A trace of everything.** Every hook, request, browser action and assertion is recorded automatically into a portable `.prototrace` file you can open in the [viewer](https://trace.prototest.dev).
- **Your runner, unchanged.** xUnit, NUnit, MSTest and TUnit are all supported.

## Where to start

| If you want to… | Read |
| --- | --- |
| try it | [Installation](./getting-started/installation.md), then [Your first test](./getting-started/first-test.md) |
| understand how it fits together | [Foundation](./foundation/overview.md) |
| test an API | [REST](./integrations/rest/index.md), [GraphQL](./integrations/graphql/index.md) |
| test a UI | [Web](./integrations/web/index.md) |
| stop hand-writing test data | [Data](./integrations/data/index.md) |
| know what your suite misses | [Coverage](./observability/coverage.md) |
| debug a failure from CI | [ProtoTrace](./observability/prototrace.md) |
| add your own integration | [Extending ProtoTest](./advanced/extending.md) |

## See it in a real suite

The repository contains a complete sample: "Northstar", an ASP.NET Core multi-tenant deployment control-plane application in `samples/ProtoTest.SampleApp`, and ten journeys in `samples/ProtoTest.Demo` that test it through REST, GraphQL (including a live subscription), gRPC, signed webhooks, spreadsheets, messaging, a real browser and in-process hosting — running in parallel, with coverage, reports and a trace. Many examples in these docs are taken from it.
