---
id: index
slug: /
sidebar_position: 0
title: Start here
sidebar_label: Start here
description: "Start with the path that matches what you bring: learn integration testing, evaluate ProtoTest, or find your way around an existing suite."
---

# Start here

import StartPaths from '@site/src/components/StartPaths';

ProtoTest is an integration testing foundation for .NET 8, 9 and 10. The integrations a suite needs share one host, one context per test, one lifecycle and one trace, so a single test can write through REST, read back through GraphQL and check the database on the way.

<StartPaths />

## Browse by task

| If you want to… | Read |
| --- | --- |
| understand how it fits together | [Foundation](./foundation/overview.md) |
| test an API | [REST](./integrations/rest/index.md), [GraphQL](./integrations/graphql/index.md) |
| test a UI | [Web](./integrations/web/index.md) |
| stop hand-writing test data | [Data](./integrations/data/index.md) |
| know what your suite misses | [Coverage](./observability/coverage.md) |
| debug a failure from CI | [ProtoTrace](./observability/prototrace.md) |
| point a coding agent at a failing run | [Agent workflows](./agent-workflows/coding-agents.md) |
| keep traces and reports in CI | [Continuous integration](./continuous-integration/index.md) |
| add your own integration | [Extending ProtoTest](./advanced/extending.md) |

## What it is made of

- **Clients** such as `Proto.Context.Rest()`, `.GraphQL()`, `.Web()` and `.Data()`, sharing the test's context.
- **Attributes** that hold reusable setup, such as a tenant or a signed-in user, and clean it up.
- **Shape assertions** that compare what matters in a response and report every mismatch at once.
- **ProtoTrace**, a portable `.prototrace` file with each test's phases, operations, checks and files, plus **coverage collectors** for OpenAPI and GraphQL.
- **Runner packages** for xUnit, NUnit, MSTest and TUnit.

## See it in a real suite

Examples in these docs come from [Northstar.ProtoTest](https://github.com/MSeys/ProtoTest/tree/version/1.1/samples/Northstar.ProtoTest), which tests the multi-tenant [Northstar sample app](https://github.com/MSeys/ProtoTest/tree/version/1.1/samples/ProtoTest.SampleApp) across API, messaging, browser, database and workbook boundaries.

OpenCSMS, an independent EV charging platform, is the full product demo: its suite runs against containers, an Aspire AppHost and a published stack, with faults injected on purpose. The [Level 5 lessons](/learn/real-topology/containers) walk through it.

![The OpenCSMS stations screen listing three stations and their charge points.](/images/opencsms/dashboard.png)
