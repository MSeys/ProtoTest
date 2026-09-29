---
sidebar_position: 5
title: Where your evidence goes
description: "One run writes one .prototrace archive and can feed five readers: the viewer, the report sinks, OpenTelemetry, the prototest CLI and the MCP server. What each surface holds, and what it cannot see."
---

# Where your evidence goes

One run writes one `.prototrace` archive, and up to five readers consume it: the [viewer](https://trace.prototest.dev), the [report sinks](./reporting.md), an [OpenTelemetry](./opentelemetry.md) backend, the [`prototest` CLI](../agent-workflows/cli.md) and the [MCP server](../agent-workflows/setup.md). They do not see the same things, so the question decides the surface.

| Surface | What it holds | What it cannot see |
| --- | --- | --- |
| **The archive** (`.prototrace`) | everything the run recorded: operations and events per test, entity state and tracked values, observations, findings, attachments, the run's identity and environment, gate verdicts and provider decisions, and the report files the sinks wrote | anything after the process. The archive is written once, at the end, so a killed run leaves none. An application's own spans are in it only when its activity sources are [configured](./prototrace.md#what-a-trace-contains). |
| **The viewer** | the archive, read entirely in your browser: the run verdict, the failing check, the per-test story, state and spans, findings and gates | the coverage the report carries. The report files travel in the archive and the viewer lists them as run attachments, but it does not render them. There are no live runs. |
| **Report sinks** (JSON and HTML) | the collectors' items, once per run: coverage totals and units, findings, run gate verdicts, resources and run metadata | the operation tree and the bytes behind it. There are no request or response bodies and no per-test story, and the report is a snapshot taken before the run's own resources release. |
| **OpenTelemetry** | the operations as spans, with their events, outcome and tags, and the application's own spans when it is instrumented | run-level evidence: gate verdicts, provider decisions and skip records, capability decisions and run resources. The run identity and environment (`runId`, `environment.*`) are archive-only, as are values above the tag cap and app-source captures you did not subscribe to. |
| **The CLI** (`prototest`) | one archive's diagnosis (`summary`), a shareable page over a folder (`index`), a report comparison (`verify`) and the feedback digest | live runs and interactive drill-down. It reads files and writes only the index page, the digests and the `--digest` file. |
| **The MCP server** | the same archive story for an agent through four read-only tools: `list_runs`, `get_failure`, `get_diagnosis` and `get_coverage` | test execution and archives outside the repositories it was pointed at. `get_coverage` reads the report the run embedded, so a run without a sink reports none. |

## Rules of thumb

- If a fact must survive the run, it belongs in the archive. The other surfaces are views of it or of the same report items.
- If a person has to decide, read the HTML report for coverage and the viewer for the story of the failing test.
- If a backend has to watch, subscribe to OpenTelemetry and keep the archive for the run-level facts spans do not carry.
- If an agent has to work, point it at the archives: the CLI, the MCP server and the viewer select the same failure from the same record.

The surfaces and their limits in full are on [ProtoTrace](./prototrace.md), [Reporting](./reporting.md) and [OpenTelemetry](./opentelemetry.md).
