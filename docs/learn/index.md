---
id: index
title: Learn integration testing
sidebar_label: Overview
sidebar_position: 0
description: "The ProtoTest learning track: why integration tests get hard, and how to make them deterministic, visible and worth trusting."
---

# Learn integration testing

The [reference](/docs/) answers what ProtoTest does. This track answers a different question: how do you get good at testing a .NET system the way it actually runs?

Integration tests touch time, shared state, real addresses, and several protocols. That is what makes them catch what unit tests cannot, and it is also why they fail in ways that look random. The lessons start from those failures and work back to the practices that prevent them. Every lesson ends with something you can see: a trace, a report, a failure message, a coverage gap.

## Who this is for

- You write unit tests and stay away from integration tests because they look flaky and opaque.
- Your suite has grown and is now slow, shared-state or hard to debug.
- You evaluate ProtoTest and want to see how it behaves when a test fails.

No ProtoTest experience is needed to start. The lessons use the Northstar.ProtoTest sample suite. They link to the reference for depth instead of repeating it.

## Start here

[The four questions](./why-integration-tests-get-hard/the-four-questions.md) opens Level 0. It names the four things every integration test has to get right, and where each one shows up in a trace.

## The curriculum

The levels are ordered. Each one names what you can do at the end and what it assumes.

| Level | Lessons | What you will be able to do | Time | Needs |
| --- | --- | --- | --- | --- |
| **0. Why integration tests get hard** | [The four questions](./why-integration-tests-get-hard/the-four-questions.md), [A failure tour](./why-integration-tests-get-hard/a-failure-tour.md), [What a test leaves behind](./why-integration-tests-get-hard/what-a-test-leaves-behind.md), [The trace as a feedback loop](./why-integration-tests-get-hard/the-trace-as-the-feedback-loop.md) | Name the four ways integration tests fail (time, state, environment, visibility), spot them in a real trace, and say what a passing test should leave behind. | about 35 minutes | nothing |
| **1. One test, one journey** | [Install and run](./one-test-one-journey/install-and-run.md), [Write your first test](./one-test-one-journey/write-your-first-test.md), [Read the trace](./one-test-one-journey/read-the-trace.md) | Run a test against an in-process application, and read the trace it left. | about 30 minutes | Level 0 |
| **2. Compose, don't glue** | [Capabilities and the host](./compose-dont-glue/capabilities-and-the-host.md), [One host, one lifetime](./compose-dont-glue/one-host-one-lifetime.md), [Add and remove an integration](./compose-dont-glue/add-and-remove-an-integration.md), [Sign in as a test user](./compose-dont-glue/sign-in-as-a-test-user.md), [When not to compose](./compose-dont-glue/when-not-to-compose.md) | Explain the run's one host and lifetime, add and remove capabilities, sign a test in as a user, and know when a problem should not go in the host at all. | about 45 minutes | Level 1 |
| **3. Determinism** | [Move the test clock](./determinism/the-test-clock.md), [Wait for readiness, not for time](./determinism/readiness-instead-of-sleeps.md), [Keep state per test and clean it up](./determinism/per-test-state-and-cleanup.md), [Parallel safety](./determinism/parallel-safety.md) | Move the clock, wait for readiness instead of sleeping, isolate per-test state and run tests in parallel. | about 30 minutes | Level 2 |
| **4. Evidence** | [Read a failing trace](./evidence/read-a-failing-trace.md), [Contract coverage, not code coverage](./evidence/contract-coverage.md), [The archive and the reports](./evidence/artifacts-and-reports.md), [Read the findings and the run gate](./evidence/read-the-findings-and-the-run-gate.md), [Take the evidence to CI](./evidence/evidence-in-ci.md) | Read a failing trace, use contract coverage next to code coverage, read the findings and the run gate, and get the report back from CI. | about 45 minutes | Level 3 |
| **5. Real topology** | [Run the suite on containers](./real-topology/containers.md), [Let Aspire start the topology](./real-topology/aspire-topology.md), [Point the suite at a real stack](./real-topology/published-mode.md), [Inject faults on purpose](./real-topology/fault-injection.md) | Run the same suite on containers, through an Aspire topology and against a real stack, and inject faults on purpose. | about 1 hour | Level 4 and an OpenCSMS checkout |
| **6. Make it yours** | [Write your own attribute](./make-it-yours/attributes.md), [Provisioners and page objects](./make-it-yours/provisioners-and-page-objects.md), [Write an integration](./make-it-yours/write-an-integration.md), [Swap a dependency for one test](./make-it-yours/swap-a-dependency-for-one-test.md), [The evidence loop with an agent](./make-it-yours/evidence-loop-with-an-agent.md) | Write your own attributes and an integration, provision data through provisioners and page objects, swap a dependency for one test, and run the evidence loop with a coding agent. | about 45 minutes | Level 5 |

The levels are ordered and each builds on the one before it.

## How a lesson works

Each lesson has the same shape:

- **Outcome.** What you will be able to do when you finish.
- **Before you start.** The lesson it builds on, and anything you need installed.
- **The scenario.** The real failure or question the lesson starts from, from the sample the lesson runs.
- **The walkthrough.** Numbered steps with real code and the real trace beside them. The page outline links to each step and the closing sections.
- **Checkpoint.** One question, the step that proves the answer, and the answer itself when you have tried.
- **What you learned.** The two or three lines worth keeping.
- **Keep exploring.** The next lesson, or the reference page for the details behind it.

The previous and next lesson links at the foot of each page follow the curriculum order.

## The sample behind the lessons

The lessons run against [Northstar.ProtoTest](https://github.com/MSeys/ProtoTest/tree/main/samples/Northstar.ProtoTest), the sample suite in this repository. It composes API, browser, database, messaging and document integrations in one host, and it ships deliberate failures: four tests that fail on purpose, one per question, next to the tests that do the same journey the right way, plus one passing journey that carries a warning. Run it with `dotnet test samples/Northstar.ProtoTest`, or read the traces without running anything. Every lesson from Level 0 to Level 4 names the trace archive it reads, and the site serves each one for download.

Level 5 leaves the sample and runs OpenCSMS, an EV charging management system and the reference suite for ProtoTest on real infrastructure. It has its own repository, which is not public yet, so those lessons need a checkout of it once it is published. Each lesson also carries a read-only walk over the committed run logs and traces, so the lesson reads without the checkout. Its suite has one Setup and four modes. Each mode runs the journeys its environment can serve.
