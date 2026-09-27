---
sidebar_position: 3
title: Frequently asked questions
sidebar_label: FAQ
description: "Short answers to the questions evaluators ask about ProtoTest: wrappers, runners, coverage, licensing, telemetry and AI usage."
---

# Frequently asked questions

Short answers to the questions people ask before adopting ProtoTest. Each one links to the proof.

## Isn't this just wrappers over existing libraries?

Yes, deliberately. ProtoTest does not reimplement HTTP, browsers, containers, messaging or database access; every [integration](./integrations/overview.md) wraps a library that already works. What ProtoTest adds is the part those libraries deliberately leave to you: one host, one [execution context](./foundation/execution-context.md), one [lifecycle](./foundation/lifecycle.md) and one [trace](./observability/prototrace.md) shared by all of them.

"The best tool per axis" is a legitimate architecture, and ProtoTest is not arguing against it. It is the answer to the glue that architecture creates. The [comparison page](./compare.md) works through this case by case.

## Do I have to adopt all the integrations?

No. There is one NuGet package per integration, and nothing forces a set. The starter template installs four - the runner adapter, [ASP.NET Core](./integrations/aspnetcore.md), [REST](./integrations/rest/index.md) and [reporting](./observability/reporting.md) - and a suite grows from there.

Adoption can also be one test. `[ProtoTest]` tests can sit next to your existing `[Test]`/`[Fact]` tests in the same project, so you can judge ProtoTest on one real scenario before moving anything.

## Does it replace Playwright, Testcontainers or EF Core?

No. Those libraries stay underneath and keep working: `ProtoTest.Web.Playwright` drives Playwright, the Testcontainers packages start real containers, and the EF Core adapter uses your `DbContext`.

They are also not hidden. Clients are registered on the context and can be retrieved directly - `Proto.Context.Client<HttpClient>("Api")`, `Proto.Context.Sql<OrdersDbContext>()`, `context.ServerFactory<Program>()` - so anything the wrappers do not expose is still reachable. See [Clients](./foundation/clients.md).

## Why not Alba? Why not Aspire testing? Why not Playwright alone?

- **Alba** is excellent at what it does - declarative HTTP scenarios with dependency-injection stubbing and deep JasperFx integration. Choose it if that is the centre of your testing. Choose ProtoTest when one test needs to cross API, database, messaging and browser in one trace. Service substitution is now shipped: `context.Override<T>()`, `[ReplaceService<T>]` and `[FailDependency<T>]` replace or fail a dependency in the application under test per test. [The full section](./compare.md#alba).
- **Aspire testing** is the better tool when deployment topology itself must be tested closed-box. ProtoTest's strength is the opposite - in-process speed and direct assertions - and the two are complementary: `ProtoTest.Aspire` can start an AppHost with the run and publish its resources as application targets. [The full section](./compare.md#aspire-testing).
- **Playwright alone** is simpler when the browser is the whole problem. ProtoTest is for when the browser is one hop in a longer journey and you want the other hops in the same test and trace. [The full section](./compare.md#playwright-net-alone).

## Which test runners work?

All five first-party adapters: NUnit, xUnit v2, xUnit v3, MSTest and TUnit. Each adapter is a package, the behavior is the same on all of them, and the [runner overview](./runners/overview.md) shows the setup class for each. Everything targets .NET 8, 9 and 10.

## Is coverage a replacement for code coverage?

No, it is a different metric. Code coverage tells you which lines ran. ProtoTest's [contract coverage](./observability/coverage.md) tells you which endpoints, responses and fields your suite **asserted** - an endpoint called by a thousand setup helpers counts as covered only when a test actually checked something about it. Both are useful; neither replaces the other.

## Why are my property hits zero?

Because receiving a field is not covering it. Property coverage comes only from the paths a shape assertion matched: a test that checks the status code covers the endpoint and the status, but none of the fields. Add the field to a `Should.MatchShape` call and it starts counting. See [reading the coverage report](./observability/coverage.md#reading-the-report) and the [shape rules](./foundation/shape-matching.md#constraints). This is deliberate - a field nobody asserts is a field that can break silently.

## What happens if the maintainer stops?

The project is designed so that it can outlive its author. It is MIT licensed, the source is public, the [trace format is documented and versioned](./observability/prototrace.md), and the [extension points](./advanced/extending.md) are part of the public surface - so a fork or a successor can keep a suite running. There is no hosted service to shut down and no account system to expire. That is not a guarantee, but it is the same argument that works against building the foundation in-house.

## Is it free?

Yes. MIT license, no paid tier, no account, no license key. The [license text](https://github.com/MSeys/ProtoTest/blob/main/LICENSE) is in the repository.

## Does ProtoTest send anything anywhere?

No. There is no telemetry, no account and no service. Traces, reports and attachments stay on the machine that wrote them, and the trace viewer is a static page that reads the file in your browser. See [Nothing phones home](./project/why-prototest.md#nothing-phones-home).

## How is AI used in this project?

Openly and heavily, for code and documentation, with the maintainer directing the work and remaining accountable for it. It is documented in full on the [AI usage page](./project/ai-usage.md), including what that choice costs.

## Something is broken. Where do I start?

The [troubleshooting page](./getting-started/troubleshooting.md) lists the common failures with their exact error strings. For anything else, ask in [Discussions](https://github.com/MSeys/ProtoTest/discussions) or [open an issue](https://github.com/MSeys/ProtoTest/issues) - the project is new and reports from real use are how it improves.
