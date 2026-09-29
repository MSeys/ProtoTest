---
sidebar_position: 1
title: Integration testing recipes
sidebar_label: Overview
description: "Scenarios that combine several ProtoTest capabilities in one test: an API call and its event, a write and its row, an API and a browser, a download and its workbook."
---

# Recipes

One test, two systems, one trace. Pick the crossing that matches your problem.

| If you need | Read this recipe | Crossing | The trace ends with |
| --- | --- | --- | --- |
| The event after a REST pay | [An API call publishes an event](./api-publishes-an-event.md) | REST writes, broker delivers | `messaging.await` plus the matched `messaging.receive` |
| The row after a REST create | [A write lands in the database](./write-lands-in-the-database.md) | REST writes, SQL stores | `sql.connection.open` in setup, released at teardown |
| The page that shows what the API made | [Created through the API, shown in the browser](./api-then-browser.md) | REST arranges, browser checks | `web.session.initialize` through `assert.web` |
| The contents of a generated file | [A downloaded report matches its model](./download-a-report.md) | REST downloads, Sheets reads | `sheets.open`, `sheets.model`, `assert.sheets` |
| The second API seeing the first one's write | [Written over REST, read over GraphQL](./rest-then-graphql.md) | REST writes, GraphQL reads | `graphql.operation` plus `assert.json.shape` |

## How to read a recipe

Situation, then code, then trace, then variations, then what the test still does not prove.

## What they have in common

- One test, one context: every client comes from `Proto.Context`.
- Arrange with an attribute or a builder, not a helper.
- One trace: the request, the event, the query and the browser steps land in the same test's story.

Each recipe follows a run from the sample suite in `samples/Northstar.ProtoTest`. Each page links to its test file. The tests use the NUnit `[ProtoTest]` attribute. Swap the attribute for another runner, as [Test runners](../runners/overview.md) describes. The test bodies stay the same.
