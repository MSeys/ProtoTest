---
id: index
slug: /
sidebar_position: 0
title: Introduction
---

# ProtoTest

ProtoTest is a composable integration-testing foundation for .NET. REST, GraphQL, browser automation, test data and in-process ASP.NET Core all attach to **one host, one execution context and one lifecycle** — so a test describes behaviour, and the infrastructure around it is written once and reused.

:::caution Work in progress
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
| know what your suite misses | [Coverage](./advanced/coverage.md) |
| debug a failure from CI | [ProtoTrace](./advanced/prototrace.md) |
| add your own integration | [Extending ProtoTest](./advanced/extending.md) |

## See it in a real suite

The repository contains a complete sample: an ASP.NET Core "control plane" application in `samples/ProtoTest.SampleApp`, and a suite in `samples/ProtoTest.Demo` that tests it through REST, GraphQL (including subscriptions and uploads), test data and in-process hosting — running in parallel, with coverage, reports and a trace. Many examples in these docs are taken from it.
