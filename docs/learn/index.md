---
id: index
title: Learn integration testing
sidebar_label: Overview
sidebar_position: 0
description: "Hands-on lessons for .NET integration testing with ProtoTest: write your first test, test real boundaries, make tests reliable, and understand why a test failed."
---

import LearnTracks from '@site/src/components/LearnTracks';

# Learn integration testing

An integration test checks that the pieces of your system work together. An API writes to a database,
a browser shows the response, or a message reaches its handler. These lessons teach you to write those tests
with ProtoTest and understand them when they fail.

Plan for about 5 to 10 minutes of reading per lesson. Running the examples may take longer.
Some lessons ask you to run or change sample code. Others walk through recorded results.

You need no ProtoTest experience to start. The [reference](/docs/) holds the details when you want them.

**New here?** Start with [Run the sample suite](./start/install-and-run.md). For an introduction to the problems
these lessons address, read [Why integration tests get hard](./why-it-gets-hard.md). Forgot a word? [Words used in
Learn](./words.md) explains each one in a sentence.

## The tracks

Start with track 1, then choose a track whose prerequisites you have covered. Each card lists them under
**Needs**. Follow the lessons within a track in order, and check each lesson for tools or earlier work it needs.

<LearnTracks />

## How a lesson works

The track lessons follow the same shape:

1. **The problem**, in a few sentences.
2. **Do it**, step by step, with real code and what you should see.
3. **What happened**, explained after you have seen it.
4. **Check yourself**, one question with the answer behind a button.
5. **Remember**, the two or three lines worth keeping.

The links at the bottom point to the next lesson or another track to explore.

## The sample behind the lessons

The lessons use [Northstar.ProtoTest](https://github.com/MSeys/ProtoTest/tree/HEAD/samples/Northstar.ProtoTest),
the sample suite in this repository. It tests one application across its API, database, browser, messages and
generated files. It includes deliberate failure drills that you enable when a lesson asks for them.

Run it with `dotnet test samples/Northstar.ProtoTest`. Many lessons also link to recorded traces you can read
without running anything. Some ask you to inspect the trace from your own run.

The **Real systems** track uses a second suite, OpenCSMS, which runs on containers and real processes. It lives in
its own repository, which is not public yet. Those lessons also work as a read-through of its recorded runs.
