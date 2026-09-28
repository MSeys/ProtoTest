---
id: index
title: Learn integration testing
sidebar_label: Overview
sidebar_position: 0
description: "The ProtoTest learning track: why integration tests get hard, and how to make them deterministic, visible and worth trusting."
---

# Learn integration testing

The [reference](/docs/) answers what ProtoTest does. This track answers a different question: how do you get good at testing a .NET system the way it actually runs?

Integration tests touch time, shared state, real addresses and more than one protocol at once. That is what makes them catch what unit tests cannot, and it is also why they fail in ways that look random. The lessons start from those failures and work back to the practices that prevent them. Every lesson ends with something you can see: a trace, a report, a failure message, a coverage gap.

## Who this is for

- You write unit tests and stay away from integration tests because they look flaky and opaque.
- Your suite has grown and is now slow, shared-state or hard to debug.
- You evaluate ProtoTest and want to see how it behaves when a test fails.

No ProtoTest experience is needed to start. The lessons use the Northstar sample that the [recipes](/docs/recipes/overview) and the reference pages also draw from, and they link into the reference for depth instead of repeating it.

## Start here

[The four questions](./why-integration-tests-get-hard/the-four-questions.md) opens Level 0. It names the four things every integration test has to get right, and where each one shows up in a trace.

## The curriculum

The levels are ordered. Each one names what you can do at the end and what it assumes.

| Level | Lessons | What you will be able to do | Time | Needs |
| --- | --- | --- | --- | --- |
| **0. Why integration tests get hard** | [The four questions](./why-integration-tests-get-hard/the-four-questions.md), [A failure tour](./why-integration-tests-get-hard/a-failure-tour.md), [What a test leaves behind](./why-integration-tests-get-hard/what-a-test-leaves-behind.md), [The trace as a feedback loop](./why-integration-tests-get-hard/the-trace-as-the-feedback-loop.md) | Name the four ways integration tests fail (time, state, environment, visibility), spot them in a real trace, and say what a passing test should leave behind. | about 40 minutes | nothing |
| **1. One test, one journey** | [Install and run](./one-test-one-journey/install-and-run.md), [Write your first test](./one-test-one-journey/write-your-first-test.md), [Read the trace](./one-test-one-journey/read-the-trace.md) | Install ProtoTest, run a test against an in-process application, and read the trace it left. | about 30 minutes | Level 0 |
| **2. Compose, don't glue** | [Capabilities and the host](./compose-dont-glue/capabilities-and-the-host.md), [Add and remove an integration](./compose-dont-glue/add-and-remove-an-integration.md), [When not to compose](./compose-dont-glue/when-not-to-compose.md) | Add and remove capabilities in the host, and know when a problem should not go in the host at all. | about 30 minutes | Level 1 |
| **3. Determinism** (4 lessons) | Not yet published | Move the clock, wait for readiness instead of sleeping, isolate per-test state and run tests in parallel. | about 1 hour | Level 2 |
| **4. Evidence** (4 lessons) | Not yet published | Read a failing trace, use contract coverage next to code coverage, and get the report back from CI. | about 1 hour | Level 3 |
| **5. Real topology** (3 lessons) | Not yet published | Run the same suite against containers and a published application, and inject faults on purpose. | about 45 minutes | Level 4 |
| **6. Make it yours** (4 lessons) | Not yet published | Write your own attributes and integrations, and run the evidence loop with a coding agent. | about 1 hour | Level 5 |

Levels 0 to 2 are live, and their lessons read top to bottom. The remaining levels follow.

## How a lesson works

Each lesson has the same shape:

- **Outcome.** What you will be able to do when you finish.
- **Before you start.** The lesson it builds on, and anything you need installed.
- **The situation.** The real failure or question the lesson starts from, from the Northstar demo.
- **The walkthrough.** Numbered steps with real code and the real trace beside them.
- **Checkpoint.** One question, the step that proves the answer, and the answer itself when you have tried.
- **What you learned.** The two or three lines worth keeping.
- **Where to go next.** The next lesson, or the reference page for the details behind it.

## The demo behind the lessons

The lessons run against [Northstar](https://github.com/MSeys/ProtoTest/tree/main/samples/Northstar.ProtoTest), the sample suite in the repository. It composes API, browser, database, messaging and document integrations in one host, and it ships deliberate failures: four tests that fail on purpose, one per question, next to the tests that do the same journey the right way. Run it with `dotnet test samples/Northstar.ProtoTest`, or read the traces without running anything. Every lesson names the archive it reads.
