---
sidebar_position: 1
title: Installation
description: "Install ProtoTest from the template, or add the runner and integration packages to a test project of your own."
---

# Installation

ProtoTest ships as small NuGet packages: one package for your test runner, `ProtoTest.Core`, and one package per integration you use. The runner and integration packages target .NET 8, 9 and 10; `ProtoTest.Cli` targets .NET 8 only, and `ProtoTest.Analyzers` and `ProtoTest.Templates` are netstandard2.0.

## Start from the template

The template creates a small ASP.NET Core API and a suite for it, already composed, traced and reported:

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop
cd Shop
dotnet test
```

What you should see: a green run. The test project leaves `Shop.Tests/bin/Debug/net10.0/TestResults/Shop.prototrace` and `Shop.html`. Open the report for the run's verdict and the routes it covered, or drop the trace on [trace.prototest.dev](https://trace.prototest.dev).

The suite is written for NUnit. Pass `--runner xunit`, `--runner xunit3`, `--runner tunit` or `--runner mstest` to generate it for another runner, and `--framework net8.0` or `--framework net9.0` to target an older framework.

Next: [Your first test](./first-test.md) walks the same path one step at a time and ends at a failure and its trace.

## Add ProtoTest to your own project

A suite is **your runner**, **`ProtoTest.Core`**, plus **one package per integration** you use. Every runner and integration depends on Core, so it arrives transitively. Add it directly when you reference the host types from your own code.

### 1. Core and your runner

```bash
dotnet add package ProtoTest.Core   # host, context, hooks, attributes, trace
dotnet add package ProtoTest.NUnit  # NUnit
```

The runner packages are `ProtoTest.NUnit`, `ProtoTest.Xunit` (xUnit v2), `ProtoTest.Xunit3` (xUnit v3), `ProtoTest.MSTest` and `ProtoTest.TUnit`. Each one needs a small setup class; see [Test runners](../runners/overview.md). The NUnit adapter needs **NUnit 4.6.1 or newer**; the standard `dotnet new nunit` template pins an older version, so update it first:

```bash
dotnet add package NUnit --version 4.6.1
```

Each runner page names its framework's floor.

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

The [integrations map](../integrations/overview.md) lists every package, including WireMock fakes, devices, Aspire and background workers.

Infrastructure the run starts and owns comes as its own package, next to the integration it serves:

```bash
dotnet add package ProtoTest.Sql.Testcontainers                 # a database container
dotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers  # a RabbitMQ container
dotnet add package ProtoTest.Sql.EntityFrameworkCore            # EF Core over the per-test connection
```

Two shared packages arrive transitively through the integrations above; add them directly only when you reference their types yourself:

```bash
dotnet add package ProtoTest.Json           # shape matching and JsonValue constraints
dotnet add package ProtoTest.Testcontainers # base class for run-scoped containers
```

### 3. Optional extras

```bash
dotnet add package ProtoTest.Reporting      # JSON and HTML reports
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

`ProtoTest.Http` is the shared HTTP client and authentication layer behind REST, GraphQL and gRPC. It is primarily an extension point for integration authors; application suites normally reference Rest, GraphQL or Grpc instead of adding it themselves.

## Browsers for Playwright

Set `InstallBrowsers` and Playwright downloads the browser it needs before the first launch, so a clean machine or CI runner needs no separate step. Alternatively, set `Channel = "msedge"` or `"chrome"` to drive a browser that is already installed. See [Web](../integrations/web/index.md).

## Where to next

- [Your first test](./first-test.md): the first test, the first failure and the trace.
- [The Learn track](/learn/one-test-one-journey/install-and-run): the same start with a real sample and recorded traces.
