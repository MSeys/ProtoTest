---
sidebar_position: 1
title: Integration testing recipes
sidebar_label: Overview
description: "Scenarios that combine ProtoTest capabilities in one test: an API call and its event, a write and its row, an API and a browser."
---

import RecipeIndex from '@site/src/components/RecipeIndex';

# Recipes

One test, two systems, one trace. Pick the crossing that matches your problem.

<RecipeIndex />

## How to read a recipe

Situation, then code, then trace, then variations, then what the test still does not prove.

## What they have in common

- One test, one context: every client comes from `Proto.Context`.
- Arrange reusable state with attributes and builders.
- One trace: the request, the event, the query and the browser steps land in the same test's story.

Each recipe follows a run from the sample suite in `samples/Northstar.ProtoTest`. Each page links to its test file. The tests use the NUnit `[ProtoTest]` attribute. Swap the attribute for another runner, as [Test runners](../runners/overview.md) describes. The test bodies stay the same.
