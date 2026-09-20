---
sidebar_position: 1
title: Integration testing recipes
sidebar_label: Overview
description: Scenarios that combine several ProtoTest capabilities in one test — an API call and its event, a write and its row, an API and a browser, a download and its workbook.
---

# Recipes

Each integration page explains one capability. Real scenarios cross several: the API call is only half of it when what matters is the event it publishes, the row it writes or the page that shows the result. These recipes show how the capabilities compose in one test — what the host needs, what the test looks like, what it proves and where it stops.

Every recipe uses one application, `Api`, and adds only what the scenario needs. Most host it in-process as in [Your first test](../getting-started/first-test.md); the browser recipe is the exception — it needs an address a browser can open. The snippets use NUnit's `[ProtoTest]`; the test bodies work unchanged with any runner — swap the attribute as described in [Test runners](../runners/overview.md).

| Recipe | Composes |
| --- | --- |
| [An API call publishes an event](./api-publishes-an-event.md) | REST, Messaging, a broker container |
| [A write lands in the database](./write-lands-in-the-database.md) | REST, SQL, Entity Framework Core, a database container |
| [Created through the API, shown in the browser](./api-then-browser.md) | Data, Web, login |
| [A downloaded report matches its model](./download-a-report.md) | REST, Sheets |
| [Written over REST, read over GraphQL](./rest-then-graphql.md) | REST, GraphQL |

## What they have in common

- **One test, one context.** Every client in a recipe comes from `Proto.Context`, created for the test and disposed after it. None of them needs a fixture of its own.
- **Setup is an attribute or a builder, not a helper.** Arranging goes through [Data](../integrations/data/index.md) or an [attribute](../foundation/attributes.md), so the next test that needs the same state reuses it by name.
- **One trace.** The request, the event, the query and the browser's steps land in the same test's story, in the order they happened. When a recipe fails, the [trace](../observability/prototrace.md) shows which half broke.

Each recipe page ends with what it does **not** prove — the assumptions a passing test leaves standing.

The report-download recipe also opens directly on its matching test in the bundled [ProtoTrace demo](https://trace.prototest.dev/?demo=1). The archive stays in the browser; the link only selects the relevant story.
