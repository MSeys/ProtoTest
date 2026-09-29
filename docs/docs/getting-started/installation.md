---
sidebar_position: 1
title: Installation
description: "Install ProtoTest from the template, or add the runner and integration packages to a test project of your own."
---

import TabbedCode from '@site/src/components/TabbedCode';

export const installTabs = [
  {
    id: 'runners',
    label: 'Runners',
    filename: 'terminal',
    language: 'bash',
    code: 'dotnet add package ProtoTest.Core   # host, context, hooks, attributes, trace\ndotnet add package ProtoTest.NUnit  # NUnit',
    footnote: 'The runner packages are ProtoTest.NUnit, ProtoTest.Xunit (v2), ProtoTest.Xunit3, ProtoTest.MSTest and ProtoTest.TUnit. Each one needs a small setup class; see Test runners.',
  },
  {
    id: 'integrations',
    label: 'Integrations',
    filename: 'terminal',
    language: 'bash',
    code: 'dotnet add package ProtoTest.AspNetCore         # host an ASP.NET Core app in-process\ndotnet add package ProtoTest.Rest               # HTTP/REST APIs\ndotnet add package ProtoTest.GraphQL            # GraphQL APIs\ndotnet add package ProtoTest.Grpc               # gRPC services\ndotnet add package ProtoTest.Web.Playwright     # browser tests with Playwright\ndotnet add package ProtoTest.Web.Selenium       # browser tests with Selenium\ndotnet add package ProtoTest.Data               # test data and provisioning\ndotnet add package ProtoTest.Sql                # a per-test database connection\ndotnet add package ProtoTest.Messaging.RabbitMq # publish and await messages on RabbitMQ\ndotnet add package ProtoTest.OpenApi            # OpenAPI contract coverage',
    footnote: 'These are the supported integrations. The preview packages (Sheets, WireMock, devices, Aspire, MassTransit, the agent layer and analyzers) install the same way; the integrations map marks each one.',
  },
  {
    id: 'infrastructure',
    label: 'Run infrastructure',
    filename: 'terminal',
    language: 'bash',
    code: 'dotnet add package ProtoTest.Sql.Testcontainers                 # a database container\ndotnet add package ProtoTest.Messaging.RabbitMq.Testcontainers  # a RabbitMQ container\ndotnet add package ProtoTest.Sql.EntityFrameworkCore            # EF Core over the per-test connection',
    footnote: 'Packages for infrastructure the run starts sit next to the integration they serve.',
  },
  {
    id: 'extras',
    label: 'Extras',
    filename: 'terminal',
    language: 'bash',
    code: 'dotnet add package ProtoTest.Reporting      # JSON and HTML reports\ndotnet add package ProtoTest.Json           # shape matching and JsonValue constraints\ndotnet add package ProtoTest.Testcontainers # base class for run-scoped containers',
    footnote: 'Json and Testcontainers arrive transitively; add them directly only when you reference their types yourself.',
  },
];

# Installation

One command starts a running suite. The template builds it, runs it and reports on it.

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop
cd Shop
dotnet test
```

The run passes. The test project leaves `Shop.Tests/bin/Debug/net10.0/TestResults/Shop.prototrace` and `Shop.html`. Open the report for the run's verdict and the routes it covered, or drop the trace on [trace.prototest.dev](https://trace.prototest.dev).

ProtoTest ships as small NuGet packages: your runner package, `ProtoTest.Core`, and one package per integration you use. The runner and integration packages target .NET 8, 9 and 10. `ProtoTest.Cli` targets .NET 8 only. `ProtoTest.Analyzers` and `ProtoTest.Templates` are netstandard2.0.

## Start from the template

The template creates a small ASP.NET Core API and a suite for it. The suite is ready to run, trace and report. It uses the commands above.

The suite is written for NUnit. Pass `--runner xunit`, `--runner xunit3`, `--runner tunit` or `--runner mstest` to generate it for another runner, and `--framework net8.0` or `--framework net9.0` to target an older framework.

### What the template creates

Two projects, one setup file picked by the runner, and four order scenarios against a tiny API:

```text
Shop/
├── Shop.slnx
├── global.json                    # xunit3 and tunit only: the Microsoft.Testing.Platform opt-in
├── README.md
├── Shop.Api/
│   ├── Shop.Api.csproj
│   ├── Program.cs                 # POST /api/orders, GET /api/orders/{id}, in-memory store
│   └── Orders.cs                  # NewOrder, Order, OrderStore
└── Shop.Tests/
    ├── Shop.Tests.csproj          # the runner package plus AspNetCore, Rest, Reporting
    ├── Setup.cs                   # hosts the API in-process, registers REST, tracing, the HTML report
    └── OrderTests.cs              # four tests: create, read back, validation, not found
```

`Setup.cs` starts as `Setup.{runner}.cs` and is renamed for the runner you picked. The test project always adds `ProtoTest.AspNetCore`, `ProtoTest.Rest` and `ProtoTest.Reporting` next to the runner package; TUnit also sets `OutputType` to `Exe`, as TUnit requires.

| Flag | Values | Default |
| --- | --- | --- |
| `--runner` | `nunit`, `xunit`, `xunit3`, `tunit`, `mstest` | `nunit` |
| `--framework` | `net10.0`, `net9.0`, `net8.0` | `net10.0` |

Next: [Your first test](./first-test.md) walks the same path one step at a time and ends at a failure and its trace.

## Add ProtoTest to your own project

| | Template (fastest) | Your own project (control) |
| --- | --- | --- |
| Start with | `dotnet new prototest -n Shop` | the `dotnet add package` lines below |
| You get | a working API, suite, trace and report | ProtoTest inside a project you already have |
| Pick the other when | you already have an application or a suite | you want the composed example to copy from |

A suite is one runner package, `ProtoTest.Core`, and one package per integration. Infrastructure hangs off the integration it serves:

```text
your suite = runner (1 of 5) + Core + 1 package per integration
  runner:        ProtoTest.NUnit, .Xunit, .Xunit3, .MSTest, .TUnit (pick one)
  integrations:  Rest, GraphQL, Grpc, Data, Sql, Sheets, OpenApi, ...
  infrastructure: Sql.Testcontainers or Messaging.RabbitMq.Testcontainers,
                  next to the Sql or Messaging integration they serve
```

Pick one package per integration you use, plus infrastructure and extras where you need them:

<TabbedCode tabs={installTabs} label="ProtoTest packages by group" />

The NUnit adapter needs **NUnit 4.6.1 or newer**; the standard `dotnet new nunit` template pins an older version, so update it first:

```bash
dotnet add package NUnit --version 4.6.1
```

## Preview packages

The preview set works but its surface can change before 1.2: Sheets, WireMock, the devices family, Aspire, the MassTransit bridge, the agent layer (Mcp, Diagnosis, Verification, Feedback, Cli) and Analyzers. Add one per need the same way:

```bash
dotnet add package ProtoTest.Sheets
dotnet add package ProtoTest.WireMock
dotnet add package ProtoTest.Aspire
```

## What comes along

| You add | You also get | Add it directly when you |
| --- | --- | --- |
| any package | `ProtoTest.Core` | your code names Core types: a hook, an attribute, a context extension |
| `ProtoTest.Rest`, `ProtoTest.GraphQL`, `ProtoTest.Grpc` | `ProtoTest.Http`, `ProtoTest.Json` | you build on the shared HTTP layer or shape constraints without those integrations |
| `ProtoTest.Sheets` | `ProtoTest.Json` | you use shape constraints outside Sheets |
| `ProtoTest.Web.Playwright`, `ProtoTest.Web.Selenium` | `ProtoTest.Web` | you write a backend on the shared web layer |
| `ProtoTest.Messaging.RabbitMq` | `ProtoTest.Messaging` | you write an adapter on the messaging layer |
| `ProtoTest.Sql.EntityFrameworkCore` | `ProtoTest.Sql` | you use the per-test connection without EF Core |
| `ProtoTest.Sql.Testcontainers`, `ProtoTest.Messaging.RabbitMq.Testcontainers` | `ProtoTest.Testcontainers` | you write a container of your own |
| `ProtoTest.OpenApi` | `ProtoTest.Rest` | you call the REST layer the coverage reads |

`ProtoTest.Http` is the shared HTTP client and authentication layer behind REST, GraphQL and gRPC. Application suites reference the integration, not the layer.

## Browsers for Playwright

Set `InstallBrowsers` to download the browser before the first launch. A clean machine or CI runner then needs no extra install step. Which backend to pick is on [Which backend](../integrations/web/index.md#which-backend).

## Where to next

- [Your first test](./first-test.md): the first test, the first failure and the trace.
- [The Learn track](/learn/one-test-one-journey/install-and-run): the same start with a real sample and recorded traces.
