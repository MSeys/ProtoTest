---
id: index
slug: /
sidebar_position: 0
title: Start here
sidebar_label: Start here
description: "Start with the path that matches what you bring: learn integration testing, evaluate ProtoTest, or find your way around an existing suite."
---

# Start here

ProtoTest is a foundation for integration testing on .NET 8, 9 and 10. You choose the integrations a suite needs, and they share the same host, test context, lifecycle and trace.

That lets one test write through REST and read through GraphQL, or combine API, browser, database, messaging and file checks when that is useful.

ProtoTest 1.0 packages are available on NuGet. The template targets `net10.0` by default; pass `--framework net8.0` or `net9.0` to use another supported target.

## Pick your path

**New to integration testing.** Start with [Learn integration testing](/learn/), then [Installation](./getting-started/installation.md) and [Your first test](./getting-started/first-test.md). The lessons begin with why integration tests get hard and end at a trace you can read.

**Evaluating ProtoTest.** [Compare ProtoTest with the alternatives](/docs/compare), read [what a run costs](/docs/benchmarks) and the [questions teams ask before adopting it](/docs/faq).

**Already have a suite.** [Recipes](./recipes/overview.md) show common journeys; [coverage](./observability/coverage.md), [ProtoTrace](./observability/prototrace.md) and [troubleshooting](./getting-started/troubleshooting.md) are the pages to reach for when a run needs explaining.

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

## Main pieces

- **Attributes** can hold reusable setup such as creating a tenant or signing in a user.
- **Clients** such as `Proto.Context.Rest()`, `.GraphQL()`, `.Web()` and `.Data()` use the same per-test context.
- **Shape assertions** compare the parts of a response that matter to the test and report all mismatches together.
- **Coverage collectors** report which parts of an OpenAPI document or GraphQL schema were called and checked.
- **ProtoTrace** records lifecycle phases, operations, checks and artifacts in a portable `.prototrace` file.
- **Runner packages** connect the same runtime to xUnit, NUnit, MSTest and TUnit.

## See it in a real suite

The repository contains [Northstar](https://github.com/MSeys/ProtoTest/tree/main/samples/ProtoTest.SampleApp), a multi-tenant sample application, and a [Learning demo](https://github.com/MSeys/ProtoTest/tree/main/samples/Northstar.ProtoTest) suite that tests it across API, messaging, browser, database and workbook boundaries. Examples throughout these docs come from that suite.
