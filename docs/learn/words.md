---
id: words
title: Words used in Learn
sidebar_label: Words used in Learn
sidebar_position: 9
description: "Every ProtoTest word the lessons use, in one plain sentence each, with the lesson that teaches it."
---

# Words used in Learn

Each lesson explains a word the first time it needs it. When a later lesson uses a word you have forgotten, look it
up here. Each entry links the lesson that teaches it.

## The run and what it shares

| Word | In plain words | Taught in |
| --- | --- | --- |
| run | One `dotnet test` invocation and everything it does. | [Run the sample suite](/learn/start/install-and-run) |
| host | The one object per test process that starts, owns and stops what the tests share. | [Know when the host starts and stops](/learn/good-tests/one-host-one-lifetime) |
| setup class | The class in your test project that tells the host what to start. | [Know when the host starts and stops](/learn/good-tests/one-host-one-lifetime) |
| integration | A ProtoTest package for one kind of system, such as REST, SQL or a browser. | [Add and remove an integration](/learn/good-tests/add-and-remove-an-integration) |
| capability | Something the run can do for a test, such as "there is a broker". A test that needs a missing capability skips. | [Read what your run provides](/learn/good-tests/capabilities-and-the-host) |
| target | A named thing the suite needs, such as the database, the broker or the API. | [Run the suite on containers](/learn/real-systems/containers) |
| provider chain | The ordered list of ways to get a target. The first one that applies wins: a configured address, then a container. | [Run the suite on containers](/learn/real-systems/containers) |
| in-process | The application runs inside the test process, so a test can reach its services and its clock. | [Read what your run provides](/learn/good-tests/capabilities-and-the-host) |
| loopback | The sample application started on a free local port, as a separate server the browser can reach. | [Wait for readiness, not for time](/learn/reliable-tests/readiness-instead-of-sleeps) |
| AppHost | An Aspire project that declares an application's processes and services, so Aspire can start them together. | [Let Aspire start the topology](/learn/real-systems/aspire-topology) |

## One test

| Word | In plain words | Taught in |
| --- | --- | --- |
| test context | The per-test object behind `Proto.Context`: this test's clients, data, identity and files. | [Write your first test](/learn/start/write-your-first-test) |
| client | The object a test uses to talk to a system, such as `Proto.Context.Rest()`. | [Write your first test](/learn/start/write-your-first-test) |
| attribute | A C# attribute on a test that prepares something before it and undoes it after. | [Write your own attribute](/learn/extend/attributes) |
| hook | Code that runs around every test, or around the whole run. | [Write an integration](/learn/extend/write-an-integration) |
| tenant | In the sample, one customer's isolated data. Each test gets its own, so tests cannot see each other's records. | [Give each test its own state](/learn/good-tests/per-test-state-and-cleanup) |
| data surface | `Proto.Context.Data()`: where a test asks for test data. A registered provisioner creates it and registers its cleanup. | [Provisioners and page objects](/learn/extend/provisioners-and-page-objects) |
| test-owned, run-owned | Who releases a resource: the test's teardown, or the host after the last test. | [Check that a test cleans up](/learn/good-tests/what-a-test-leaves-behind) |

## The trace

| Word | In plain words | Taught in |
| --- | --- | --- |
| trace | The `.prototrace` file a run writes: a zip with everything the run did. | [See why a test failed](/learn/start/read-the-trace) |
| viewer | The page at trace.prototest.dev that opens a trace in your browser. | [See why a test failed](/learn/start/read-the-trace) |
| operation | One recorded step: a call, a check, a setup step. Its kind, such as `http.request`, says what sort of step. | [See why a test failed](/learn/start/read-the-trace) |
| phase | Setup, execution or teardown: the three parts of every test in the trace. | [See why a test failed](/learn/start/read-the-trace) |
| run layer | The part of the trace for work outside any test: the host's capabilities, resources and releases. | [Know when the host starts and stops](/learn/good-tests/one-host-one-lifetime) |
| check | An assertion as the trace records it, with what it expected and what it read. | [Read a failing trace](/learn/understand-failures/read-a-failing-trace) |
| attachment | A file kept in the trace, such as a response body or a downloaded workbook. | [Check that a test cleans up](/learn/good-tests/what-a-test-leaves-behind) |
| finding | Something the run reports that is not the test result, such as a teardown error. | [Why a passing test can still leave an error](/learn/understand-failures/findings) |
| run gate | A check over the whole run after the last test. A failed gate fails the run. | [Why the run is red when every test is green](/learn/understand-failures/run-gates) |
| digest | The short failure summary the `prototest` command prints, and CI posts as a comment. | [Keep the evidence when CI fails](/learn/understand-failures/evidence-in-ci) |
| MCP | Model Context Protocol: how a coding agent calls tools. ProtoTest's MCP server lets it read traces. | [Run the evidence loop with an agent](/learn/extend/evidence-loop-with-an-agent) |
