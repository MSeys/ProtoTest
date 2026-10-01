---
id: why-it-gets-hard
title: Why integration tests get hard
sidebar_label: Why it gets hard
sidebar_position: 1
description: "The four things an integration test has to get right: time, state, environment and visibility, each with one example and the track that teaches it."
---

# Why integration tests get hard

An integration test talks to parts of a system that really run: an API, a database, a broker, a browser, a clock you do not own. That makes it valuable. It also makes it fail in ways a unit test never does.

Most of those failures look alike. A test fails once, passes on the retry, and the message tells you nothing you can act on. Something outside the tested code changed, and the test never said what it depended on.

Four things cause most of this: time, state, environment and visibility. This page names each one with an example. The sample suite in the next lessons contains each example as a test that fails on purpose, next to the test that holds.

## Time

**Question: who moves the clock?**

A test waits one real second and expects a subscription to be past due. The application still reports it as active. The application reads a test clock, and waiting does not move that clock. The check fails on `$.status`: it expected `past_due` and read `active`.

The fix moves the clock from the test: `Proto.Context.Clock.Advance(TimeSpan.FromDays(8))`. Then the application and the check read the same moment.

Learn it in [Reliable tests](/learn/reliable-tests/the-test-clock).

## State

**Question: what does the test share with others?**

A test reads the project with id `prj_1`. The request returns 404, because no test in the run created that id. Another run may have created it, or nobody did.

The fix creates the project inside the test, so the data belongs to that test and is removed when it ends.

Learn it in [Good tests](/learn/good-tests/per-test-state-and-cleanup).

## Environment

**Question: where does the address come from?**

A test opens a raw `HttpClient` on `127.0.0.1:5099`. It works on one machine, the one where it was written. It also runs outside the framework, so the trace holds no request at all.

The fix asks the run for a client with `Proto.Context.Rest()`, which takes its address from the composition. The same test then runs in-process, in a container, or against a published environment.

Learn it in [Good tests](/learn/good-tests/capabilities-and-the-host) and [Reliable tests](/learn/reliable-tests/readiness-instead-of-sleeps).

## Visibility

**Question: what can the test show when it fails?**

A test sends an empty project name and asserts only the status. It expected `201 Created` and the check reported `400`. The response body said `validation_failed` and named the empty parameter, but nothing read it.

The fix asserts the problem body, so the same failure names the code and the message.

Learn it in [Understand failures](/learn/understand-failures/a-failure-tour).

## What a passing test has answered

A test you can trust has answered all four. It moves the clock, creates and removes its own data, takes its address from the run, and asserts something that names the difference when it fails. A failure is usually one of these answers missing.

Next: [Run the sample suite](/learn/start/install-and-run).
