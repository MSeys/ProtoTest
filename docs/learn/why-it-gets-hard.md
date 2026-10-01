---
id: why-it-gets-hard
title: Why integration tests get hard
sidebar_label: Why it gets hard
sidebar_position: 1
description: "Four questions about time, state, environment and visibility, each with an example and the track that teaches it."
---

# Why integration tests get hard

An integration test checks how running parts of a system work together, such as an API and a database. It can expose problems that a test of one isolated component would miss.

A test may fail once and pass on retry without explaining what changed. The cause might be application code, or a dependency the test did not control or describe.

This page introduces four questions to ask: about time, state, environment and visibility. The sample suite pairs each example with a deliberate failure and a passing test. The later lessons show how to run these drills or read their recorded traces.

## Time

**Question: who moves the clock?**

A test issues an invoice, waits one real second and expects the subscription to be past due. The application still reports it as active. It reads a test clock, which advances only when the test moves it. The check fails on `$.status`: it expected `past_due` and read `active`.

The fix advances that clock beyond the invoice's due date: `Proto.Context.Clock.Advance(TimeSpan.FromDays(8))`. The application then reports `past_due`, as the test expects.

Learn it in [Reliable tests](/learn/reliable-tests/the-test-clock).

## State

**Question: what does the test share with others?**

A test reads the project with id `prj_1` without creating it. The request returns 404. A hardcoded id does not establish that the record exists or belongs to this test.

The fix creates a project inside the test's own tenant, an organization prepared during setup. It checks that the tenant's project list contains that project. Teardown deletes the tenant and its data.

Learn it in [Good tests](/learn/good-tests/per-test-state-and-cleanup).

## Environment

**Question: where does the address come from?**

A test opens a raw `HttpClient` on `127.0.0.1:5099`, assuming an application is listening there. In the recorded drill, the connection fails. The raw client bypasses ProtoTest's request recording, so the trace has no request operation for that call.

The fix uses the test's client, `Proto.Context.Rest()`. It takes its address from the suite's composition: the setup that chooses applications, dependencies and their addresses. The test can then use the environment selected by that setup without hardcoding an address.

Learn it in [Good tests](/learn/good-tests/capabilities-and-the-host) and [Reliable tests](/learn/reliable-tests/readiness-instead-of-sleeps).

## Visibility

**Question: what can the test show when it fails?**

A test sends an empty project name and asserts only the status. It expected `201 Created` and the check reported `400`. The response body said `validation_failed` and named the empty parameter. ProtoTest includes that body in the failure message, but the test did not check it.

The passing test expects `400 Bad Request` and checks the problem body's code and message. It verifies that the application rejected the empty name for the expected reason.

Learn it in [Understand failures](/learn/understand-failures/a-failure-tour).

## What a passing test has answered

Apply these questions to the dependencies your test uses. For time-dependent behavior, decide how the test controls or observes time. For mutable data, decide who creates it, who can see it and who removes it. Choose addresses through the suite's setup, and make assertions that explain a mismatch.

Not every test needs to move a clock or create data. State those responsibilities where they apply, so a failure gives you evidence to investigate.

Next: [Run the sample suite](/learn/start/install-and-run).
