---
sidebar_position: 4
title: Roadmap
description: "What is committed next in ProtoTest, what is being explored, and what is deliberately not planned - with the reasoning behind each."
---

# Roadmap

This page says what shipped, what is coming, what is being considered, and what will not happen. It follows one rule. Nothing unshipped is described as a feature.

Shipped: the current release. Next: the release follow-through, and nothing else. Exploring: three ideas. Never: six deliberate noes, each with its reason.

The order below is the order work happens in. Work happens in effort order, not date order. ProtoTest is maintained in personal time. The [changelog](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md) records what actually shipped.

## Shipped

The current release made ProtoTest a platform proven on a real product. The sections below stay visible as user-facing capabilities. The full list is the [changelog](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md).

<details>
<summary>What the release shipped, in one list</summary>

- **Shape and truth:** the package review, with the `ProtoTest.OpenTelemetry` package retired into the docs.
- **Devices on a real product:** the reference demo's OCPP gateway and charge-point simulator, and the WebSocket device backend proven on it.
- **Product surface:** the demo's dashboard, browser journeys and the monthly export through Sheets.
- **More layers testable:** per-test service substitution and fault injection, expected and exhaustive shape assertions, traffic coverage, `Should` vocabulary parity, a built-in test user, WireMock, Aspire and MassTransit integrations, and template `--runner` variants.
- **Topology under test:** container topology, published mode, fault injection and the nightly reference run.
- **A second device protocol:** MQTT, after OCPP proved the transport model.
- **Devices that send data strings:** TCP (connecting out or listening) and serial transports over shared stream framing, and typed messages that write and read a device's data strings.
- **Agent and evidence layer:** `ProtoTest.Mcp`, the `prototest` CLI, `ProtoTest.Diagnosis`, `ProtoTest.Verification`, `ProtoTest.Feedback`, the [ProtoTest Evidence action](https://github.com/MSeys/prototest-action), the static trace index, and the docs and skills pages.
- **Showcase:** the benchmark republished on the reference demo and the trace showpiece.

The reference demo is the proof. It is a separate open-source EV-charging platform with a REST API, PostgreSQL, a billing worker, browser journeys and containers.

</details>

When an item ships, it leaves this section and becomes a docs page like everything else.

## Next: committed, in this order

- **Release follow-through:** the tag, the packages, the public default branch, the API reference and the site are the release.

Nothing else is committed. An item moves out of **Exploring** when someone needs it in a real suite.

Preview packages graduate the same way. The agent layer, devices, Sheets, Aspire, WireMock, MassTransit and analyzers stay preview until real suites prove them. Then they join the supported set with no breaking change. The [support policy](./sustainability.md) is the same best-effort one for everyone.

## Exploring: genuinely considered, not committed

| Idea | What would move it |
| --- | --- |
| **Wolverine.Tracking bridge**, the same shape as the MassTransit bridge | Users asking for it |
| **Allure and ReportPortal sinks**, built on the existing sink contract | The integration template existing, then community interest |
| **Further device transports** (Sigfox, TLS for TCP) and MQTT variants | A request, interest or need from a real suite |

## Not planned, with reasons

- **A hosted service, account system or team server.** The design is local evidence. Traces, reports and the viewer work without a server, and that is what makes the data safe to keep.
- **A commercial CSMS or device hardware certification.** The reference demo is a proof of the framework, not a product with customers.
- **Support for .NET Framework or runtimes before .NET 8.** The test runner and integration ecosystems ProtoTest builds on have moved on.
- **Reimplementing Playwright, Testcontainers, EF Core or any other underlying library.** Wrapping them and keeping their escape hatches reachable is the design.
- **Conflating contract coverage with code coverage.** They answer different questions, and the [coverage docs](../observability/coverage.md) explain both rather than merging them.
- **Renaming ProtoTest.** The name is settled.

## If you want something on this page

Open an [issue](https://github.com/MSeys/ProtoTest/issues) and describe the problem, not the API. Demand decides what leaves **Exploring**: items move when someone needs them in a real suite.
