---
sidebar_position: 2
title: ProtoTest vs Testcontainers, Alba and Aspire testing
sidebar_label: ProtoTest compared
description: "An honest comparison: where ProtoTest wins against the alternatives, where each alternative wins, and when ProtoTest is not the right choice."
---

import Comparison from '@site/src/components/Comparison';
import {comparisonConcerns, withoutProtoTest, withProtoTest} from '@site/src/data/comparison';

# ProtoTest vs Testcontainers, Alba and Aspire testing
Every tool on this page is good, and ProtoTest is built on several of them. Your application runs in-process with `WebApplicationFactory`. Infrastructure comes from Testcontainers. Browsers use Playwright or Selenium. The question is never whether those libraries work. It is which shape fits your suite.

This page says where each alternative wins, where ProtoTest wins, and ends with the cases where ProtoTest is the wrong choice. [When ProtoTest is not the right choice](#when-prototest-is-not-the-right-choice) is the honest version up front.

| If you | Pick |
| --- | --- |
| Only test HTTP | `WebApplicationFactory` plus an assertion library |
| Live in declarative HTTP scenarios with DI stubbing | Alba |
| Need deploy fidelity | Aspire testing |
| Only drive a browser | Playwright alone |
| Cross API, database, broker and browser in one test | ProtoTest |

## What stays in the test

ProtoTest moves setup to the suite host and attributes, and the scenario lines stay the same. Below is one test against the same in-process application, written as a regular NUnit fixture and with ProtoTest. Both send the same requests and check the same values. The marked lines are **plumbing**: they start the application, create clients, provision the tenant and user, authenticate, deserialize, clean up, or are the file's own scaffolding. Everything else is the scenario.

<Comparison without={withoutProtoTest} with={withProtoTest} concerns={comparisonConcerns} />

## WebApplicationFactory, Testcontainers, Verify and Shouldly

The established .NET combination: host the application with `WebApplicationFactory`, start PostgreSQL and RabbitMQ with Testcontainers, assert responses with Shouldly and snapshot them with Verify. ProtoTest wraps exactly these primitives. The difference is what connects them.

**Where they win**

- Nothing new to adopt. Each library is maintained by people who focus on one thing, and you can replace any of them independently.
- There is no lifecycle to learn beyond the primitives themselves. A team that knows `WebApplicationFactory` is productive on day one.
- You assemble the diagnostics you need, library by library: container logs here, a snapshot there, a browser trace when a browser is involved.

What adopting ProtoTest costs: one setup class per test project, one package per integration, and three ideas to learn (the host, the lifecycle, the trace). Tests swap `[Test]` or `[Fact]` for `[ProtoTest]`, select the application with `[Application]`, and reach clients through `Proto.Context`. One test beside your existing suite costs five minutes ([your first test](../getting-started/first-test.md)). A suite pays that learning cost once, and then per test only the attribute and accessor lines.

**Where ProtoTest wins**

- One host, one [execution context](../foundation/execution-context.md) and one [lifecycle](../foundation/lifecycle.md) across every integration a test uses. The API, the database, the broker and the browser share the setup that already ran.
- One [`.prototrace`](../observability/prototrace.md) per run, with the operations of every integration and the failing assertion in place. Read it in the static [trace viewer](https://trace.prototest.dev), or summarize it with `prototest summary` in CI.
- [Contract coverage](../observability/coverage.md): which endpoints, responses and fields the suite actually asserted. The four-library stack does not provide it.
- [Provisioners and seed data](../integrations/data/provisioners.md) as reusable setup instead of helpers that grow inside the test project.
- Five [test-runner adapters](../runners/overview.md): the same suite shape works across runners.

If your suite is already shaped this way and the glue does not hurt, keep it. The glue is the product here.

## Alba

Alba is the closest neighbour. It is a mature declarative testing library for ASP.NET Core with a `Scenario` DSL, service registration stubbing, and deep integration with the Critter Stack (Marten and Wolverine).

**Where they win**

- Maturity and focus. Over a decade of releases, a large body of examples, a stable public API.
- Declarative HTTP scenarios with built-in dependency-injection and authentication stubbing.
- First-class depth in the JasperFx ecosystem that ProtoTest does not target.

**Where ProtoTest wins**

- Scope beyond HTTP. One test can write through REST, read through [GraphQL](../integrations/graphql/index.md), check the [database](../integrations/sql/index.md), publish through [messaging](../integrations/messaging/index.md) and drive a [browser](../integrations/web/index.md), all in one [trace](../observability/prototrace.md).
- [Coverage](../observability/coverage.md), [provisioning](../integrations/data/provisioners.md) and [reporting](../observability/reporting.md) belong to the foundation instead of being assembled per project.
- The trace is a versioned, documented file ([format 2.0](../observability/prototrace.md)) with a static viewer, so a CI failure is readable where it happened.
- Runner adapters for each framework rather than one testing style.

For per-test substitution use `context.Override<T>()`, `[ReplaceService<T>]` or `[FailDependency<T>]`. Each test gets a dedicated server, so an override does not leak into the next test. Alba remains stronger for suites built entirely around declarative HTTP scenarios with DI stubbing.

## Aspire testing

.NET Aspire composes a distributed application from an AppHost and can launch that topology inside a test.

**Where they win**

- Real topology. Every service and dependency runs as its own process, so the suite sees the system closed-box, exactly as it deploys.
- Microsoft backing, first-class tooling and the .NET 10 toolchain.
- The best choice when deployment fidelity itself is what you are testing.

**Where ProtoTest wins**

- In-process speed and direct assertions. Services run inside the test process where the test can inspect them, and [contract coverage](../observability/coverage.md) can see every response.
- No AppHost requirement. In-process hosting with Testcontainers runs on .NET 8, 9 and 10 today, with less CI machinery.
- The same suite can point at a deployed environment through [configuration](../getting-started/environments.md) when closed-box fidelity is what a given run needs.

These are complementary rather than competing. Keep Aspire where deployment topology matters, and a ProtoTest suite can test the same system from outside. `ProtoTest.Aspire` composes an AppHost through the same infrastructure hooks and publishes its resources as application targets.

## Playwright .NET alone

**Where they win**

- Best-in-class browser automation: tracing, code generation, auto-waiting, maintained by Microsoft.
- If your tests only ever need a browser, it is the shortest path, with no other layer to learn.

**Where ProtoTest wins**

- The browser is one integration among several. `ProtoTest.Web.Playwright` (or the Selenium backend) drives the same flows. The API call that arranged the data and the database check that proves it landed are in the same test and the same trace.
- A Playwright trace shows what the browser did. A [`.prototrace`](../observability/prototrace.md) shows the browser alongside everything that led to it.
- Login, page objects and [attachments](../foundation/attachments.md) belong to the same lifecycle rather than a separate helper layer.

If browser testing is the whole problem, Playwright alone is simpler. If the browser is one hop in a longer journey, ProtoTest keeps the hops together.

## Building the foundation in-house

Some teams build their own integration-testing layer instead. [I did too](./why-prototest.md).

**Where it wins**

- Perfect fit for your domain vocabulary, your infrastructure and your priorities, with nobody else's roadmap to negotiate.
- Total control: any behavior can be changed the day it annoys you.

**Where ProtoTest wins**

- The same idea already exists in public, with documentation, tests and a [changelog](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md).
- It survives people. An in-house framework usually stops being maintained when its author changes teams. ProtoTest is MIT, the [trace format is documented](../observability/prototrace.md) and [extension points](../advanced/extending.md) are public.
- Diagnostics you would rarely fund yourself: the trace, the viewer, contract coverage and reports.
- Runner adapters and container-backed infrastructure that you would otherwise rebuild one integration at a time.

A framework you own completely is the right answer when the problem is truly yours alone. When it is the same problem everybody has, sharing the foundation is cheaper.

## Support and maintenance

| Project | Maintained by | Issues and help | Fix line |
| --- | --- | --- | --- |
| ProtoTest | One person, in personal time. No company, no team, no sponsors. | Bugs go to GitHub Issues, questions to Discussions. Best effort, with no response time guarantee. | Fixes land on the newest released line. Older lines get security or critical fixes case by case, never promised. There is no LTS line. |
| ASP.NET Core, Playwright, Aspire testing | Microsoft teams behind the .NET stack. | Vendor backed channels plus large public trackers. | LTS releases on the .NET schedule. |
| Alba | The JasperFx community and the Critter Stack, with over a decade of releases. | A public tracker with a long history of answered questions. | A stable public API with a long track record. |
| Testcontainers | The Testcontainers organization, with a commercial offering beside the open source libraries. | A public tracker plus vendor backed channels. | Maintained release lines. |

The full ProtoTest story is on the [support and sustainability page](./sustainability.md).

## When ProtoTest is not the right choice

- **You only test HTTP endpoints.** `WebApplicationFactory` plus an assertion library is smaller and has no lifecycle to learn.
- **You want zero framework dependencies.** Every ProtoTest integration is an opinionated wrapper. That is the design, and it is fair to decline it.
- **Closed-box topology is the requirement.** Aspire testing tests processes as they deploy. ProtoTest's in-process strengths are the wrong fit for that.
- **You need per-test DI-level stubbing over a declarative HTTP scenario.** ProtoTest's substitution works and is traced, but a library built entirely around scenario stubbing has a smaller surface.
- **You are not on .NET 8, 9 or 10.** ProtoTest targets those three runtimes, and earlier frameworks are out of scope.
- **Your team already has a foundation it likes.** Do not replace working infrastructure to gain a trace.

And the honest caveat in the other direction: ProtoTest is young. The alternatives have larger ecosystems, longer track records and more answers on the internet. If community size decides it for you, that is a fair reason to choose them.

If you want to judge it with your own problem, one test beside your existing suite costs five minutes: [your first test](../getting-started/first-test.md). The packages are per-integration, and they do not replace the libraries underneath them. If you adopt and later change your mind, [Leaving ProtoTest](./leaving.md) removes it one test at a time.
