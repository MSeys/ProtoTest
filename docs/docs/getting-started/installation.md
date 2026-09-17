---
sidebar_position: 1
title: Installation
---

# Installation

ProtoTest is a set of small packages: **one for your test runner**, plus **one per integration** you use. `ProtoTest.Core` comes along automatically — every runner and integration depends on it.

ProtoTest targets **.NET 8, 9 and 10**.

## 1. Your runner

```bash
dotnet add package ProtoTest.NUnit      # NUnit
dotnet add package ProtoTest.Xunit3     # xUnit v3
dotnet add package ProtoTest.Xunit      # xUnit v2
dotnet add package ProtoTest.MSTest     # MSTest
dotnet add package ProtoTest.TUnit      # TUnit
```

Each one needs a small setup class — see [Test Runners](../runners/overview.md).

## 2. Your integrations

```bash
dotnet add package ProtoTest.Rest               # HTTP/REST APIs
dotnet add package ProtoTest.GraphQL            # GraphQL APIs
dotnet add package ProtoTest.AspNetCore         # host an ASP.NET Core app in-process
dotnet add package ProtoTest.Web.Playwright     # browser tests with Playwright
dotnet add package ProtoTest.Web.Selenium       # browser tests with Selenium
dotnet add package ProtoTest.Data               # test data and provisioning
dotnet add package ProtoTest.OpenApi            # OpenAPI contract coverage
```

Some bring others with them, so you never add these yourself:

| You add | You also get |
| --- | --- |
| any package | `ProtoTest.Core` |
| `ProtoTest.Rest`, `ProtoTest.GraphQL` | `ProtoTest.Http`, `ProtoTest.Json` |
| `ProtoTest.Web.Playwright`, `ProtoTest.Web.Selenium` | `ProtoTest.Web` |
| `ProtoTest.OpenApi` | `ProtoTest.Rest` |

## 3. Optional extras

```bash
dotnet add package ProtoTest.Reporting          # JSON and HTML reports
dotnet add package ProtoTest.OpenTelemetry      # export operations to OpenTelemetry
```

## Browsers for Playwright

Playwright downloads its browsers separately, once per machine. After building the test project:

```bash
pwsh bin/Debug/net10.0/playwright.ps1 install
```

Alternatively, set `options.Channel = "msedge"` or `"chrome"` to drive a browser that's already installed. See [Web](../integrations/web/index.md#playwright).

## Next

[Your first test](./first-test.md) walks through a complete suite, step by step.
