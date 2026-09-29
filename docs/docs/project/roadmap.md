---
sidebar_position: 4
title: Roadmap
description: "What is committed next in ProtoTest, what is being explored, and what is deliberately not planned - with the reasoning behind each."
---

# Roadmap

This page says what shipped, what is coming, what is being considered, and what will not happen. It follows the project's one rule: nothing is advertised as existing before it does, and every unshipped item is described as a plan, not a feature.

The order below is the order work happens in. It is effort-ordered, not date-ordered - ProtoTest is maintained in personal time, and a solo calendar is not a promise. The [changelog](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md) records what actually shipped.

## Shipped: 1.1

The 1.1 line made ProtoTest a platform proven on a real product:

- **Shape and truth:** the package review, with the `ProtoTest.OpenTelemetry` package retired into the docs.
- **Devices on a real product:** the reference demo's OCPP gateway and charge-point simulator, and the WebSocket device backend proven on it.
- **Product surface:** the demo's dashboard, browser journeys and the monthly export through Sheets.
- **More layers testable:** per-test service substitution and fault injection, expected and exhaustive shape assertions, traffic coverage, `Should` vocabulary parity, a built-in test user, WireMock, Aspire and MassTransit integrations, and template `--runner` variants.
- **Topology under test:** container topology, published mode, fault injection and the nightly reference run.
- **A second device protocol:** MQTT, after OCPP proved the transport model.
- **Agent and evidence layer:** `ProtoTest.Mcp`, the `prototest` CLI, `ProtoTest.Diagnosis`, `ProtoTest.Verification`, `ProtoTest.Feedback`, the feedback action, the static trace index, and the docs and skills pages.
- **Showcase:** the benchmark republished on the reference demo, the trace showpiece, and the rewritten Northstar suite as the Learning demo (the reference demo is the full product demo).

The reference demo is the proof: an independent open-source EV-charging platform (CSMS) in its own repository, with a REST API, PostgreSQL, a billing worker with retry and dead-letter semantics, browser journeys, device protocols and containers, running suites this framework did not write for itself.

When an item ships, it leaves this section and becomes a docs page like everything else.

## Next: committed, in this order

- **Publish and follow through:** push the 1.1 tag and packages, bring the public default branch to the release, regenerate the API reference, and deploy the site.

Nothing else is committed. An item moves out of **Exploring** when someone needs it in a real suite.

## Exploring: genuinely considered, not committed

- **Wolverine.Tracking bridge** - the same shape as the MassTransit bridge, only if users ask for it.
- **Allure and ReportPortal sinks** - built on the existing sink contract; community-friendly once the integration template exists.
- **Further device transports** (TCP, serial, Sigfox) and MQTT variants - after the first OCPP suite and the MQTT backend have real users.

## Not planned, with reasons

- **A hosted service, account system or team server.** The design is local evidence: traces, reports and the viewer work without a server, and that is what makes the data safe to keep.
- **A commercial CSMS or device hardware certification.** The reference demo is a proof of the framework, not a product with customers.
- **Support for .NET Framework or runtimes before .NET 8.** The test runner and integration ecosystems ProtoTest builds on have moved on.
- **Reimplementing Playwright, Testcontainers, EF Core or any other underlying library.** Wrapping them and keeping their escape hatches reachable is the design.
- **Conflating contract coverage with code coverage.** They answer different questions; the [coverage docs](../observability/coverage.md) explain both rather than merging them.
- **Renaming ProtoTest.** The name is settled.
- **Device transports beyond WebSocket and MQTT** (TCP, serial, Sigfox) until a real user needs one.

## If you want something on this page

Open an [issue](https://github.com/MSeys/ProtoTest/issues) and describe the problem, not the API. Demand decides what leaves **Exploring**: items move when someone needs them in a real suite, and the [support policy](./sustainability.md) is the same best-effort one for everyone.
