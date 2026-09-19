---
sidebar_position: 1
title: Overview
description: Scenarios that combine several ProtoTest capabilities in one test — an API call and its event, a write and its row, an API and a browser, a download and its workbook.
---

# Recipes

Each integration page explains one capability. Real scenarios cross several: the API call is only half of it when what matters is the event it publishes, the row it writes or the page that shows the result. These recipes show how the capabilities compose in one test — what the host needs, what the test looks like, and what to watch for.

Every recipe uses one application, `Api`, hosted in-process as in [Your first test](../getting-started/first-test.md), and adds only what the scenario needs.

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
