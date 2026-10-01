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

You can start from a template that gives you a working API and test suite, or add ProtoTest packages to a test project you already have. The template is the faster way to see ProtoTest work.

## Start from the template

The template creates a small ASP.NET Core API and a test suite for it. The default template needs the .NET 10 SDK.

```bash
dotnet new install ProtoTest.Templates
dotnet new prototest -n Shop
cd Shop
dotnet test
```

When it works, the tests pass. The run also leaves three files in `Shop.Tests/bin/Debug/net10.0/TestResults/`: `prototest-{runId}.prototrace`, `Shop.json` and `Shop.html`. The first is the run's **trace**, the record of everything the run did. The other two are the report, as JSON for tools and as HTML for you. Each run writes its own trace, so a rerun never overwrites the previous one.

To look at the results, open the report. It shows the verdict and the API routes the run covered. You can also drop the trace on [trace.prototest.dev](https://trace.prototest.dev), the viewer for traces.

The suite is written for NUnit. Two flags change that:

| Flag | Values | Default |
| --- | --- | --- |
| `--runner` | `nunit`, `xunit`, `xunit3`, `tunit`, `mstest` | `nunit` |
| `--framework` | `net10.0`, `net9.0`, `net8.0` | `net10.0` |

Next, [Your first test](./first-test.md) builds the same suite one step at a time and ends with a failure and its trace.

<details>
<summary>What the template creates</summary>

Two projects, one setup file for the runner you picked, and four order tests against a tiny API:

```text
Shop/
├── Shop.slnx
├── global.json                    # xunit3 and tunit only: the Microsoft.Testing.Platform opt-in
├── README.md
├── AGENTS.md                      # how a coding agent runs, reads and extends the suite
├── .mcp.json                      # registers the ProtoTest MCP server for Claude Code
├── .config/dotnet-tools.json      # pins the prototest CLI and the prototest-mcp server
├── .claude/skills/                # prototest-evidence-loop and prototest-write-test
├── Shop.Api/
│   ├── Shop.Api.csproj
│   ├── Program.cs                 # POST /api/orders, GET /api/orders/{id}, in-memory store
│   └── Orders.cs                  # NewOrder, Order, OrderStore
└── Shop.Tests/
    ├── Shop.Tests.csproj          # the runner package plus AspNetCore, Rest, Reporting, Analyzers
    ├── Setup.cs                   # hosts the API in-process, registers REST, tracing, the reports
    └── OrderTests.cs              # four tests: create, read back, validation, not found
```

`Setup.cs` starts as `Setup.{runner}.cs` and is renamed for the runner you picked. The test project always adds `ProtoTest.AspNetCore`, `ProtoTest.Rest` and `ProtoTest.Reporting` next to the runner package, and `ProtoTest.Analyzers` for the build-time warnings. TUnit also sets `OutputType` to `Exe`, because TUnit requires it.

The agent files cost nothing until you use them. Run `dotnet tool restore` once and a coding agent that reads `.mcp.json` can list the runs and read each failure through the [MCP server](../agent-workflows/setup.md). Delete them if you do not use an agent.

</details>

## Add ProtoTest to your own project

Use this path when you already have an application or a test project. A ProtoTest suite is one runner package, `ProtoTest.Core`, and one package for each kind of system you test.

```text
your suite = runner (1 of 5) + Core + 1 package per integration
  runner:        ProtoTest.NUnit, .Xunit, .Xunit3, .MSTest, .TUnit (pick one)
  integrations:  Rest, GraphQL, Grpc, Data, Sql, Sheets, OpenApi, ...
  infrastructure: Sql.Testcontainers or Messaging.RabbitMq.Testcontainers,
                  next to the Sql or Messaging integration they serve
```

An **integration** is a package for one kind of system, such as REST, SQL or a browser. **Infrastructure** is something the run starts for you, such as a database container. Pick the packages you need from the tabs:

<TabbedCode tabs={installTabs} label="ProtoTest packages by group" />

The NUnit package needs **NUnit 4.6.1 or newer**. The standard `dotnet new nunit` template pins an older version, so update it first:

```bash
dotnet add package NUnit --version 4.6.1
```

After the packages are in, each runner needs a small setup class. [Your first test](./first-test.md#2-configure-the-host) shows it for NUnit, and [Test runners](../runners/overview.md) covers the others.

## Browsers for Playwright

Set `InstallBrowsers` to make the run download the browser before the first launch. A clean machine or CI runner then needs no extra install step. To choose between Playwright and Selenium, see [Which backend](../integrations/web/index.md#which-backend).

## Preview packages

Some packages work but may change their API before the next minor release. These are Sheets, WireMock, the devices family, Aspire, the MassTransit bridge, the agent layer (Mcp, Diagnosis, Verification, Feedback, Cli) and Analyzers. You install them the same way:

```bash
dotnet add package ProtoTest.Sheets
dotnet add package ProtoTest.WireMock
dotnet add package ProtoTest.Aspire
```

## Target frameworks

The runner and integration packages target .NET 8, 9 and 10. `ProtoTest.Cli` targets .NET 8 only. `ProtoTest.Analyzers` and `ProtoTest.Templates` target netstandard2.0.

<details>
<summary>Which packages come along automatically</summary>

Adding a package also adds the shared layer it builds on. Add that layer yourself only when your own code uses its types.

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

</details>

## Where to next

- [Your first test](./first-test.md): the first test, the first failure and the trace.
- [The Learn track](/learn/start/install-and-run): the same start with a real sample and recorded traces.
