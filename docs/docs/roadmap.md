---
sidebar_position: 2
title: Roadmap
description: "What is committed next in ProtoTest, what is being explored, and what is deliberately not planned - with the reasoning behind each."
---

# Roadmap

This page says what is coming, what is being considered, and what will not happen. It follows the project's one rule: nothing is advertised as existing before it does, and every unshipped item is described as a plan, not a feature.

The order below is the order work happens in. It is effort-ordered, not date-ordered - ProtoTest is maintained in personal time, and a solo calendar is not a promise. The [changelog](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md) records what actually shipped.

## Next — committed, in this order

### 1.1 — polish and trust

The current line, in preparation. Most of the evaluator work is already live: the [comparison page](./compare.md), the [FAQ](./faq.md) and [support and sustainability](./project/sustainability.md) are published. Still to come in this line:

- **Published overhead numbers** versus raw `WebApplicationFactory` on the [benchmarks page](./benchmarks.md), including where ProtoTest is slower.
- **Versioned documentation** at the 1.1 release, so 1.0 links keep working.
- **Trace tooling** already in review: the `prototest` CLI's `trace summary`, the documented [format compatibility policy](./observability/prototrace.md#format-compatibility), and a pack-time compatibility gate that fails an unintentional API break.
- **Polish that the suite asked for**: path-based single-value reads from JSON responses, a test clock over `TimeProvider` for time-dependent rules, run metadata for cross-run correlation in CI, and failure-mode tests for startup and mid-run container death.

### 1.2 — hosting, and the reference demo

- **Background-worker hosting** - running a generic host or hosted service under the suite with the same lifecycle and trace as the API.
- **Readiness waiting** - `AwaitReady`-style health checks instead of sleeps in setup.
- **Per-test service substitution and fault injection** - the deliberate gap named on the [comparison page](./compare.md#alba).
- **The reference demo, milestone 1**: an independent open-source EV-charging platform (CSMS) in its own repository - REST API, Postgres, a billing worker with retry and dead-letter semantics, and the first end-to-end journey. It exists to prove the framework on requirements that were not written for it.

### 1.x — the platform line

- **An OCPP gateway and a WebSocket device backend** - the demo's charge-point simulator is the demand proof for device testing.
- **Dashboard and browser journeys** on the demo, including the monthly export through Sheets.
- **WireMock integration** for external-dependency fakes, and **an Aspire adapter** for closed-box container-topology runs - both validated by the demo's later milestones.
- **The demo's benchmarks**: a seeded 1,000-journey run that republishes the overhead numbers on a real product.

Each of these is in the internal plan with acceptance criteria; when one ships, it leaves this section and becomes a docs page like everything else.

## Exploring — genuinely considered, not committed

- **MCP server and coding-agent workflow.** The reader half exists: `ProtoTest.Traces` and the `prototest` CLI. A local MCP server over traces and reports is the natural next step; it will get a docs page only when it is real.
- **MassTransit bridge** - wrapping `ITestHarness` in the existing messaging client surface, the way other integrations wrap their libraries.
- **Wolverine.Tracking bridge** - the same shape, only if users ask for it.
- **Analyzers package** - the useful rules are intent-dependent (a `[ProtoTest]` test sitting next to plain `[Test]` methods, a context client used with nothing registered). Design first; a noisy analyzer is worse than none.
- **Traffic coverage** - marking fields that arrived in a response as *observed but unasserted*, in a separate report section. Never counted as covered; the assertion-level rule stays.
- **Exhaustive assertion mode** - flagging fields present in a response that no shape mentioned. One implementation, shared with expected-shape-in-the-call.
- **Allure and ReportPortal sinks** - built on the existing sink contract; community-friendly once the integration template exists.
- **A static trace index** for sharing a folder of traces without a server.
- **An MQTT device backend** - after the WebSocket backend has a real protocol and the first device suite exists.

## Not planned — with reasons

- **A hosted service, account system or team server.** The design is local evidence: traces, reports and the viewer work without a server, and that is what makes the data safe to keep.
- **A commercial CSMS or device hardware certification.** The reference demo is a proof of the framework, not a product with customers.
- **Support for .NET Framework or runtimes before .NET 8.** The test runner and integration ecosystems ProtoTest builds on have moved on.
- **Reimplementing Playwright, Testcontainers, EF Core or any other underlying library.** Wrapping them and keeping their escape hatches reachable is the design.
- **Conflating contract coverage with code coverage.** They answer different questions; the [coverage docs](./observability/coverage.md) explain both rather than merging them.
- **Renaming ProtoTest.** The name is settled.
- **Device transports beyond WebSocket and MQTT** (TCP, serial, Sigfox) until a real user needs one.

## If you want something on this page

Open an [issue](https://github.com/MSeys/ProtoTest/issues) and describe the problem, not the API. Demand decides what leaves **Exploring**: items move when someone needs them in a real suite, and the [support policy](./project/sustainability.md) is the same best-effort one for everyone.
