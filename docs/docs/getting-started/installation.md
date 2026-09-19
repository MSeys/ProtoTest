---
sidebar_position: 1
title: Installation
description: Start from the ProtoTest template, or add the runner and integration packages to a test project of your own.
---

# Installation

ProtoTest targets **.NET 8, 9 and 10**, and every package ships as a prerelease (`0.1.0-alpha`) — so every `dotnet add package` line below uses `--prerelease`. The `prototest` template scaffolds `net10.0` unless you pass `--framework net8.0` or `net9.0`.

## Quick start: the template

The quickest way in is a solution that already works: a small ASP.NET Core API and a suite for it, composed, traced and reported.

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop
cd Shop
dotnet test
```

The run leaves `TestResults/Shop.prototrace` and `TestResults/Shop.html` in the test project's output folder — `Shop.Tests/bin/Debug/net10.0/TestResults/`; the template renames them to whatever you pass to `-n`. Drop the trace onto [trace.prototest.dev](https://trace.prototest.dev), then read [Your first test](./first-test.md) to see how each part is built.

## Going further: add ProtoTest to your own project

ProtoTest is a set of small packages: **your test runner**, **`ProtoTest.Core`**, plus **one package per integration** you use. Every runner and integration depends on Core, so it arrives transitively — add it directly when you want to reference the host types from your own code.

### 1. Core and your runner

```bash
dotnet add package ProtoTest.Core --prerelease   # host, context, hooks, attributes, trace
dotnet add package ProtoTest.NUnit --prerelease  # NUnit
```

The runner packages are `ProtoTest.NUnit`, `ProtoTest.Xunit` (xUnit v2), `ProtoTest.Xunit3` (xUnit v3), `ProtoTest.MSTest` and `ProtoTest.TUnit`. Each one needs a small setup class — see [Test runners](../runners/overview.md).

### 2. Your integrations

```bash
dotnet add package ProtoTest.AspNetCore         --prerelease  # host an ASP.NET Core app in-process
dotnet add package ProtoTest.Rest               --prerelease  # HTTP/REST APIs
dotnet add package ProtoTest.GraphQL            --prerelease  # GraphQL APIs
dotnet add package ProtoTest.Grpc               --prerelease  # gRPC services
dotnet add package ProtoTest.Web.Playwright     --prerelease  # browser tests with Playwright
dotnet add package ProtoTest.Web.Selenium       --prerelease  # browser tests with Selenium
dotnet add package ProtoTest.Data               --prerelease  # test data and provisioning
dotnet add package ProtoTest.Sql                --prerelease  # a per-test database connection
dotnet add package ProtoTest.Messaging.RabbitMq --prerelease  # publish and await messages on RabbitMQ
dotnet add package ProtoTest.Sheets             --prerelease  # assert on generated spreadsheets
dotnet add package ProtoTest.OpenApi            --prerelease  # OpenAPI contract coverage
```

Infrastructure the run starts and owns comes as its own package, next to the integration it serves:

```bash
dotnet add package ProtoTest.Sql.Testcontainers                 --prerelease  # a database container
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers  --prerelease  # a RabbitMQ container
dotnet add package ProtoTest.Sql.EntityFrameworkCore            --prerelease  # EF Core over the per-test connection
```

Two shared packages arrive transitively through the integrations above; add them directly only when you reference their types yourself:

```bash
dotnet add package ProtoTest.Json           --prerelease  # shape matching and JsonValue constraints
dotnet add package ProtoTest.Testcontainers --prerelease  # base class for run-scoped containers
```

### 3. Optional extras

```bash
dotnet add package ProtoTest.Reporting          --prerelease  # JSON and HTML reports
dotnet add package ProtoTest.OpenTelemetry      --prerelease  # export operations to OpenTelemetry
```

## What comes along

| You add | You also get |
| --- | --- |
| any package | `ProtoTest.Core` |
| `ProtoTest.Rest`, `ProtoTest.GraphQL`, `ProtoTest.Grpc` | `ProtoTest.Http`, `ProtoTest.Json` |
| `ProtoTest.Sheets` | `ProtoTest.Json` |
| `ProtoTest.Web.Playwright`, `ProtoTest.Web.Selenium` | `ProtoTest.Web` |
| `ProtoTest.Messaging.RabbitMq` | `ProtoTest.Messaging` |
| `ProtoTest.Sql.EntityFrameworkCore` | `ProtoTest.Sql` |
| `ProtoTest.Sql.Testcontainers`, `ProtoTest.Messaging.RabbitMq.Testcontainers` | `ProtoTest.Testcontainers` |
| `ProtoTest.OpenApi` | `ProtoTest.Rest` |

`ProtoTest.Http` is the shared HTTP client and authentication layer for REST, GraphQL and gRPC. It is primarily an extension point for integration authors; application suites normally reference Rest, GraphQL or Grpc instead of adding it themselves.

## Browsers for Playwright

Set `InstallBrowsers` and Playwright downloads the browser it needs before the first launch, so a clean machine or CI runner needs no separate step. Alternatively, set `Channel = "msedge"` or `"chrome"` to drive a browser that's already installed. See [Web](../integrations/web/index.md).

## Next

[Your first test](./first-test.md) walks through a complete suite, step by step.
