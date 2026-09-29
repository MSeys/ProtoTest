---
sidebar_position: 2
title: Vocabulary
description: "The words the rest of the documentation uses: host, execution context, capability, address, provider chain, lifecycle phases, trace entity kinds, and observations against findings and attachments."
---

# Vocabulary

The docs use a small, deliberate vocabulary. This page is the whole of it, one table per idea. The name in prose is the name the trace records. Setup, report and viewer use the same term.

## The first hour

| Term | What it means |
| --- | --- |
| **Host** | One `ProtoHost` per test process, built once by your runner's [assembly setup](../runners/overview.md). It owns the dependency injection container, the run hooks, the run gates, the infrastructure and the start and completion of every test. |
| **Execution context** | One `ProtoExecutionContext` per test, reached anywhere on the test's flow as `Proto.Context`. It holds that test's clients, typed state, resources, findings, attachments and observations, and its own DI scope. |
| **Address** | Where a target is reached. An application derives its key, `ProtoTest:Applications:{name}:BaseUrl`; infrastructure and integrations declare their own, such as `ConnectionStrings:*` or the broker's connection key. |

## Foundation introduces

| Term | What it means |
| --- | --- |
| **Configured provider** | the chain provider that holds when the environment already configures the target's keys (`UseConfigured()`). The environment serves the target, so the run starts nothing for it. |
| **Capability** | A named thing the host can actually serve, described by a `ProtoCapabilityDescriptor` with a kind, a name and, when it covers one instance, that instance. An integration declares one only while it is composed and can serve it, so [skip conditions](./skip-conditions.md) can gate tests on it. |
| **Provider chain** | The ordered providers registered for one target. The first whose condition holds serves it, and every other provider is recorded skipped with its reason. A target with no satisfied provider fails the build. See [Environment resolution](./environment-resolution.md). |
| **Trace entity** | One thing whose state the run recorded: a client, the context, the test user, the server, a capability, a clock, a device. Operations point at entities by id, so an entry names what it acted on. |

## Lifecycle phases

Every trace entry belongs to one phase:

| Phase | What runs |
| --- | --- |
| `Setup` | the test hooks, then the attributes, before the body |
| `Execution` | the test body |
| `Teardown` | attributes and hooks in reverse, attachments publish, resources release |
| `Rollback` | teardown after a failed setup, in place of `Teardown` |
| `Run` | run-level work: run hooks, gates and infrastructure |

The order, the failure rules and the rollback walk are in [Host and lifecycle](./lifecycle.md).

## Trace entity kinds

Entities are state, not history: each appears once in the archive with its latest state and its versions.

| Kind | Id | What it records |
| --- | --- | --- |
| `client` | `client:{fullTypeName}:{name}` | a client a test resolved |
| `context` | `{fullTypeName}`, or `{key}:{fullTypeName}` when set with a key | typed state an attribute or hook set |
| `auth` | `auth:user` | the test user and how it signed in |
| `server` | `server:{entryPointFullName}` | the in-process application server |
| `capability` | `{kind}:{name}`, with `:{instance}` when the descriptor carries one | what the run can serve |
| `clock` | `clock:run` for the run, and a clock per test | the clocks a test can advance |
| `device` | `device:{client}:{deviceType}:{id}` | a device session |

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
