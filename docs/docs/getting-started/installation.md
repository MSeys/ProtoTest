---
sidebar_position: 1
title: Installation
description: Start from the ProtoTest template, or add the runner and integration packages to a test project of your own.
---

# Installation

ProtoTest targets **.NET 8, 9 and 10**.

## Start from the template

The quickest way in is a solution that already works: a small ASP.NET Core API and a suite for it, composed, traced and reported.

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop
cd Shop
dotnet test
```

The run leaves `Shop.prototrace` and `Shop.html` in `Shop.Tests/bin/Debug/net10.0/TestResults/`. Drop the trace onto [trace.prototest.dev](https://trace.prototest.dev), then read [Your first test](./first-test.md) to see how each part is built. Use `--framework net8.0` or `net9.0` for an older runtime.

## Add ProtoTest to your own project

ProtoTest is a set of small packages: **one for your test runner**, plus **one per integration** you use. `ProtoTest.Core` comes along automatically — every runner and integration depends on it.

### 1. Your runner

```bash
dotnet add package ProtoTest.NUnit      # NUnit
dotnet add package ProtoTest.Xunit3     # xUnit v3
dotnet add package ProtoTest.Xunit      # xUnit v2
dotnet add package ProtoTest.MSTest     # MSTest
dotnet add package ProtoTest.TUnit      # TUnit
```

Each one needs a small setup class — see [Test runners](../runners/overview.md).

### 2. Your integrations

```bash
dotnet add package ProtoTest.AspNetCore         # host an ASP.NET Core app in-process
dotnet add package ProtoTest.Rest               # HTTP/REST APIs
dotnet add package ProtoTest.GraphQL            # GraphQL APIs
dotnet add package ProtoTest.Grpc               # gRPC services
dotnet add package ProtoTest.Web.Playwright     # browser tests with Playwright
dotnet add package ProtoTest.Web.Selenium       # browser tests with Selenium
dotnet add package ProtoTest.Data               # test data and provisioning
dotnet add package ProtoTest.Sql                # a per-test database connection
dotnet add package ProtoTest.Messaging.RabbitMq # publish and await messages on RabbitMQ
dotnet add package ProtoTest.Sheets             # assert on generated spreadsheets
dotnet add package ProtoTest.OpenApi            # OpenAPI contract coverage
```

Infrastructure the run starts and owns comes as its own package, next to the integration it serves:

```bash
dotnet add package ProtoTest.Sql.Testcontainers                 # a database container
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers  # a RabbitMQ container
dotnet add package ProtoTest.Sql.EntityFrameworkCore            # EF Core over the per-test connection
```

Some bring others with them, so you never add these yourself:

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

### 3. Optional extras

```bash
dotnet add package ProtoTest.Reporting          # JSON and HTML reports
dotnet add package ProtoTest.OpenTelemetry      # export operations to OpenTelemetry
```

## Browsers for Playwright

Set `InstallBrowsers` and Playwright downloads the browser it needs before the first launch, so a clean machine or CI runner needs no separate step. Alternatively, set `Channel = "msedge"` or `"chrome"` to drive a browser that's already installed. See [Web](../integrations/web/index.md#browsers).

## Next

[Your first test](./first-test.md) walks through a complete suite, step by step.
