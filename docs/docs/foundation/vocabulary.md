---
sidebar_position: 2
title: Vocabulary
description: "Every ProtoTest word the docs and the lessons use, in one plain sentence, with the lesson that teaches it and the page with the details."
---

import AnnotatedCode from '@site/src/components/AnnotatedCode';

export const entityCode = `{
  "id": "client:System.Net.Http.HttpClient:Northstar",
  "client.name": "Northstar",
  "client.owned": true,
  "client.initializer": "AspNetCoreClientInitializer\`1",
  "resource.state": "released"
}
{
  "id": "Northstar.ProtoTest.NorthstarMemberContext",
  "context.type": "Northstar.ProtoTest.NorthstarMemberContext",
  "context.value": { "Id": "owner", "Token": "[REDACTED]" }
}
{
  "kind": "http.request",
  "name": "REST · POST /api/v1/projects",
  "entity": "client:System.Net.Http.HttpClient:Northstar",
  "http.response.status_code": 201
}`;

export const entityCallouts = [
  {
    line: 1,
    title: 'A client entity',
    note: 'The in-process client the test used. Owned by the test, created by the ASP.NET Core initializer, released at teardown.',
  },
  {
    line: 8,
    title: 'A context entity',
    note: 'Typed state the member attribute set. Secrets are redacted in the archive, so the token reads [REDACTED].',
  },
  {
    line: 13,
    title: 'The operation that points at it',
    note: 'The POST names the client entity by id and carries what it returned: status 201.',
  },
];

# Vocabulary

Every ProtoTest word the docs and the lessons use, in one plain sentence. Each row links the lesson that teaches
the word and the page with the details. The trace, the reports and the viewer use the same words.

## The run and what it shares

| Word | In plain words | Learn it | Details |
| --- | --- | --- | --- |
| **run** | One `dotnet test` invocation and everything it does. | [Run the sample suite](/learn/start/install-and-run) | [Host and lifecycle](./lifecycle.md) |
| **host** | The one `ProtoHost` per test process. It starts what the tests share, owns it and stops it after the last test. | [Know when the host starts and stops](/learn/good-tests/one-host-one-lifetime) | [Host and lifecycle](./lifecycle.md) |
| **setup class** | The class in your test project that tells the host what to start. Your runner calls it once. | [Know when the host starts and stops](/learn/good-tests/one-host-one-lifetime) | [Runners](../runners/overview.md) |
| **integration** | A ProtoTest package for one kind of system, such as REST, SQL or a browser. | [Add and remove an integration](/learn/good-tests/add-and-remove-an-integration) | [Integrations](../integrations/overview.md) |
| **capability** | Something the run can do for a test, such as "there is a broker". A test that needs a missing capability skips with a reason. | [Read what your run provides](/learn/good-tests/capabilities-and-the-host) | [Skip conditions](./skip-conditions.md) |
| **target** | A named thing the suite needs, such as the database, the broker or the API. | [Run the suite on containers](/learn/real-systems/containers) | [Environment resolution](./environment-resolution.md) |
| **address** | Where a target is reached, read from configuration, such as `ProtoTest:Applications:{name}:BaseUrl` or a connection string. | [Point the suite at a real stack](/learn/real-systems/published-mode) | [Environment resolution](./environment-resolution.md) |
| **provider chain** | The ordered ways to get one target. The first that applies wins, such as a configured address before a container. | [Run the suite on containers](/learn/real-systems/containers) | [Environment resolution](./environment-resolution.md) |
| **in-process** | The application runs inside the test process, so a test can reach its services and its clock. | [Read what your run provides](/learn/good-tests/capabilities-and-the-host) | [ASP.NET Core](../integrations/aspnetcore.md) |
| **loopback** | The application started on a free local port as a separate server, for a browser to reach. | [Wait for readiness, not for time](/learn/reliable-tests/readiness-instead-of-sleeps) | [Infrastructure](./infrastructure.md) |
| **AppHost** | An Aspire project that declares an application's processes and services, so they start together. | [Let Aspire start the topology](/learn/real-systems/aspire-topology) | [Aspire](../integrations/aspire.md) |
| **run gate** | A check over the whole run after the last test. A failed gate fails the run, even when every test passed. | [Why the run is red when every test is green](/learn/understand-failures/run-gates) | [Host and lifecycle](./lifecycle.md) |

## One test

| Word | In plain words | Learn it | Details |
| --- | --- | --- | --- |
| **test context** | The per-test `ProtoExecutionContext` behind `Proto.Context`: this test's clients, data, identity and files. | [Write your first test](/learn/start/write-your-first-test) | [Test context](./execution-context.md) |
| **client** | The object a test uses to talk to a system, such as `Proto.Context.Rest()`. | [Write your first test](/learn/start/write-your-first-test) | [Clients](./clients.md) |
| **attribute** | A C# attribute on a test that prepares something before it and undoes it after. | [Write your own attribute](/learn/extend/attributes) | [Attributes](./attributes.md) |
| **hook** | Code that runs around every test, or around the whole run. | [Write an integration](/learn/extend/write-an-integration) | [Hooks](./hooks.md) |
| **data surface** | `Proto.Context.Data()`: where a test asks for test data. A registered provisioner creates it and registers its cleanup. | [Create test data with a provisioner](/learn/extend/provisioners) | [Provisioners](../integrations/data/provisioners.md) |
| **test-owned, run-owned** | Who releases a resource: the test's teardown, or the host after the last test. | [Check that a test cleans up](/learn/good-tests/what-a-test-leaves-behind) | [Infrastructure recipes](./infrastructure-recipes.md) |
| **tenant** | In the Northstar sample, one customer's isolated data. Each test gets its own. | [Give each test its own state](/learn/good-tests/per-test-state-and-cleanup) | |

## The trace

| Word | In plain words | Learn it | Details |
| --- | --- | --- | --- |
| **trace** | The `.prototrace` file a run writes: a zip with everything the run did. | [See why a test failed](/learn/start/read-the-trace) | [ProtoTrace](../observability/prototrace.md) |
| **viewer** | The page at [trace.prototest.dev](https://trace.prototest.dev) that opens a trace in your browser. | [See why a test failed](/learn/start/read-the-trace) | [ProtoTrace](../observability/prototrace.md) |
| **operation** | One recorded step: a call, a check, a setup step. Its kind, such as `http.request`, says what sort of step. | [See why a test failed](/learn/start/read-the-trace) | [ProtoTrace](../observability/prototrace.md#what-a-trace-contains) |
| **phase** | Setup, execution or teardown: the parts of every test in the trace. | [See why a test failed](/learn/start/read-the-trace) | [Lifecycle phases](#lifecycle-phases) below |
| **run layer** | The part of the trace that belongs to no single test: the host's capabilities, resources and releases. | [Know when the host starts and stops](/learn/good-tests/one-host-one-lifetime) | [Lifecycle phases](#lifecycle-phases) below |
| **trace entity** | One thing whose state the run recorded, such as a client or the test user. Operations point at it by id. | [Check that a test cleans up](/learn/good-tests/what-a-test-leaves-behind) | [Trace entity kinds](#trace-entity-kinds) below |
| **check** | An assertion as the trace records it, with what it expected and what it read. | [Read a failing trace](/learn/understand-failures/read-a-failing-trace) | [Assertions](./assertions.md) |
| **observation, finding, attachment** | A fact a test learned, something worth reporting that is not the result, and a file the test kept. | [Why a passing test can still leave an error](/learn/understand-failures/findings) | [Three different things](#observations-findings-and-attachments) below |
| **digest** | The short failure summary `prototest summary` prints, and CI posts as a comment. | [Keep the evidence when CI fails](/learn/understand-failures/evidence-in-ci) | [Diagnosis](../agent-workflows/diagnosis.md) |
| **MCP** | Model Context Protocol: how a coding agent calls tools. ProtoTest's MCP server lets it read traces. | [Run the evidence loop with an agent](/learn/extend/evidence-loop-with-an-agent) | [Agent setup](../agent-workflows/setup.md) |

## Lifecycle phases

Every trace entry belongs to one phase:

| Phase | What runs |
| --- | --- |
| `Setup` | the test hooks, then the attributes, before the body |
| `Execution` | the test body |
| `Teardown` | attributes and hooks in reverse, attachments publish, resources release |
| `Rollback` | teardown after a failed setup, in place of `Teardown` |
| `Run` | run-level work: run hooks, gates and infrastructure. The viewer calls this the run layer. |

The order, the failure rules and the rollback walk are in [Host and lifecycle](./lifecycle.md).

## Trace entity kinds

Entities are state, not history: each appears once in the archive with its latest state and its versions. An
operation names the entity it acted on, so a reader can follow a call to the client that made it:

<AnnotatedCode
  filename="state.json (l1-first-journey)"
  code={entityCode}
  callouts={entityCallouts}
  foot={<>Two entities and the operation that points at them, from <code>docs/static/lessons/l1-first-journey.prototrace</code>.</>}
/>

Example ids come from the lesson archives (`l1-first-journey`, `l3-clock-window`).

| Kind | Id | Example id | What it records |
| --- | --- | --- | --- |
| `client` | `client:{fullTypeName}:{name}` | `client:System.Net.Http.HttpClient:Northstar` | a client a test resolved |
| `context` | `{fullTypeName}`, or `{key}:{fullTypeName}` when set with a key | `Northstar.ProtoTest.NorthstarMemberContext` | typed state an attribute or hook set |
| `auth` | `auth:user` | `auth:user` | the test user and how it signed in |
| `server` | `server:{entryPointFullName}:{application}` | `server:ProtoTest.SampleApp.Program:Northstar` | the in-process application server |
| `capability` | `{kind}:{name}`, with `:{instance}` when the descriptor carries one | `server:ASP.NET Core:Northstar` | what the run can serve |
| `clock` | `clock:run` for the run, and a clock per test | `clock:725654000001` | the clocks a test can advance |
| `device` | `device:{client}:{deviceType}:{id}` | a pattern; no lesson archive records one | a device session |

Tracked values are state items with kind `value` and an id of the form `{type}:{identity}`. Infrastructure and resources use their own ids. The entry kinds themselves, from `test.setup` to the last assertion, are listed in [ProtoTrace](../observability/prototrace.md#what-a-trace-contains).

## Observations, findings and attachments

Three different things, three different destinations:

| | Observation | Finding | Attachment |
| --- | --- | --- | --- |
| What it is | a fact a test learned | something worth reporting that is not a failure | a file a test produced |
| Who records it | integrations; you with `RecordObservation` | you with `AddFinding`; a teardown failure becomes one | integrations; you with `AddAttachment` |
| Where it goes | collectors, then reports, and the trace | reports and run gates, and the trace | the runner, and the archive |
| What it does not do | fail a test or a gate | replace the test's outcome | count as coverage |

Coverage is built from observations, which is why this distinction matters: a fact a test did not state is not coverage. See [Coverage and observations](../observability/coverage.md) and [Attachments](./attachments.md).
