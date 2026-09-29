---
sidebar_position: 1
title: Integration testing recipes
sidebar_label: Overview
description: "Scenarios that combine several ProtoTest capabilities in one test: an API call and its event, a write and its row, an API and a browser, a download and its workbook."
---

# Recipes

An integration page explains one capability. Real scenarios cross several. The API call is only half of the story when what matters is the event it publishes, the row it writes or the page that shows the result.

These recipes show the capabilities composed in one test: what the host needs, what the test looks like, what the trace shows, what else the pieces can do, and what the test still does not prove.

Every recipe is the journey as the demo suite runs it in `samples/Northstar.ProtoTest`, with the test file one link away. Where a recipe links the viewer, the trace is the demo's own generated run. The tests use NUnit's `[ProtoTest]`; the bodies work unchanged with any runner when you swap the attribute as [Test runners](../runners/overview.md) describes.

| Recipe | Composes |
| --- | --- |
| [An API call publishes an event](./api-publishes-an-event.md) | REST, Messaging, a broker the run owns |
| [A write lands in the database](./write-lands-in-the-database.md) | REST, SQL, the store the run shares |
| [Created through the API, shown in the browser](./api-then-browser.md) | REST, Web, a loopback listener |
| [A downloaded report matches its model](./download-a-report.md) | REST, Sheets |
| [Written over REST, read over GraphQL](./rest-then-graphql.md) | REST, GraphQL |

## How to read a recipe

Each recipe has the same four parts:

- **The situation.** Why one capability is not enough for this task.
- **The code.** The composition the scenario needs and the test that runs it.
- **What the trace shows.** The operations the journey records, in the order the test caused them.
- **The variations.** The alternatives the same scenario allows, such as a container or a configured address.

Every recipe ends with **what it does not prove**: the assumptions a passing test leaves standing.

## What they have in common

- **One test, one context.** Every client comes from `Proto.Context`, created for the test and released after it. None of them needs a fixture of its own.
- **Arranging is an attribute or a builder, not a helper.** State goes through [Data](../integrations/data/index.md) or an [attribute](../foundation/attributes.md), so the next test that needs the same state reuses it by name.
- **One trace.** The request, the event, the query and the browser steps land in the same test's story, in the order they happened. When a recipe fails, the [trace](../observability/prototrace.md) shows which half broke.

The report-download recipe also opens directly on its matching test in the bundled [ProtoTrace demo](https://trace.prototest.dev/?demo=1). The archive stays in the browser; the link only selects the story.
