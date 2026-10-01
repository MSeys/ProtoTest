---
id: index
title: Learn integration testing
sidebar_label: Overview
sidebar_position: 0
description: "Hands-on lessons for .NET integration testing with ProtoTest: write your first test, test real boundaries, make tests reliable, and understand why a test failed."
---

import LearnTracks from '@site/src/components/LearnTracks';

# Learn integration testing

An integration test checks that the pieces of your system work together: the API writes to the database, the
browser shows what the API returned, a message reaches its handler. These lessons teach you to write such tests
with ProtoTest, and to understand them when they fail.

Each lesson takes 5 to 10 minutes and teaches one skill. You run real code from a sample suite, see the result,
and then read what happened. You need no ProtoTest experience. The [reference](/docs/) holds the details when you
want them.

**New here?** Start with [Run the sample suite](./start/install-and-run.md). If you first want to know why
integration tests are worth the effort, read [Why integration tests get hard](./why-it-gets-hard.md).

## The tracks

Start with track 1. After that, take the tracks in any order. Each card says what it assumes.

<LearnTracks />

## How a lesson works

Every lesson has the same shape, so you always know where you are:

1. **The problem**, in a few sentences.
2. **Do it**, step by step, with real code and what you should see.
3. **What happened**, explained after you have seen it.
4. **Check yourself**, one question with the answer behind a button.
5. **Remember**, the two or three lines worth keeping.

The links at the bottom of each page take you to the next lesson in the track.

## The sample behind the lessons

The lessons use [Northstar.ProtoTest](https://github.com/MSeys/ProtoTest/tree/version/1.1/samples/Northstar.ProtoTest),
the sample suite in this repository. It tests one application across its API, database, browser, messages and
generated files. Some of its tests fail on purpose, so the lessons have real failures to look at.

Run it with `dotnet test samples/Northstar.ProtoTest`. You can also read its recorded traces without running
anything: each lesson links to the trace it uses.

The **Real systems** track uses a second suite, OpenCSMS, which runs on containers and real processes. It lives in
its own repository, which is not public yet. Those lessons also work as a read-through of its recorded runs.
