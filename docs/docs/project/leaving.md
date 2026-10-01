---
sidebar_position: 9
title: Leaving ProtoTest
description: "How to unadopt ProtoTest: remove the host, swap the attributes and assertions back, and keep the traces and reports."
---

# Leaving ProtoTest

No lock in was ever the goal. If the foundation stops fitting, you can take it out one test at a time. There is no migration tool. The steps below are the whole runbook.

## Remove the host

Delete the setup class that builds the host and the package references your suite no longer needs. Tests that lose the host lose `Proto.Context`, the shared lifecycle, and the trace. Remove them in the same commit so nothing references a host that no longer exists.

## Swap the attributes and assertions back

Each test changes the same way:

1. Swap `[ProtoTest]` back for the runner attribute (`[Test]`, `[Fact]`, and the rest).
2. Remove `[Application]` and the other suite attributes, and move the setup they owned back into fixtures or helpers.
3. Replace `Proto.Context` clients with plain clients (an `HttpClient`, the container client, the driver) and the `Should` assertions with the library you used before.

One test at a time keeps the suite green while you go. The [comparison page](./compare.md) lists what you give up at each step: the shared lifecycle first, then the trace, then the coverage.

## Keep the traces and reports

What you recorded stays yours. `.prototrace` files open in the static viewer with no account and no service to shut off. HTML and JSON reports are plain files beside them. Keep a reader from the same era as the traces: the [compatibility matrix](../observability/prototrace-archive.md#format-compatibility) states which reader opens which version.

## What stays coupled

Two things follow you out:

- Era locked readers. The library and the CLI read spans 2.x and state 1.x, and the viewer also reads state 2.x. A trace outside those versions needs a reader from its own era. Archive the viewer build or the CLI version with traces you must keep.
- OpenTelemetry gaps. The backend export carries operations and events only. Large values, gate verdicts, run identity and environment, and target resolutions stay archive only (the [full list](../observability/opentelemetry.md#what-does-not-reach-the-backend)). If dashboards depend on those facts, move them before you switch the export off.

## The honest effort

The cost is linear in test count. Each test pays the attribute, client, and assertion swap above, plus whatever shared setup moves back into helpers. A small suite converts in an afternoon. A large one converts incrementally, one fixture at a time, with both styles running side by side until the last host is gone.
