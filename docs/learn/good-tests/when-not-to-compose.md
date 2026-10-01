---
id: when-not-to-compose
title: Decide what belongs in the host
sidebar_label: When not to compose
sidebar_position: 7
description: "Decide whether a piece belongs to the host, to a test, or to neither, and recognize the three common ways suites get it wrong."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# Decide what belongs in the host

<Lesson
  track="Write good integration tests"
  step="Lesson 7 of 7"
  minutes={8}
  outcomes={[
    'Decide whether a piece belongs to the host, to a test, or to neither',
    'Choose explicit ownership and cleanup for a test resource',
    'Choose when a resource should live for one test or the run',
  ]}
  needs={[
    <>Lesson 3, <Link to="/learn/good-tests/add-and-remove-an-integration">Add and remove an integration</Link></>,
    'Nothing installed. The archives are on this site',
  ]}
/>

## The problem

Adding infrastructure to host setup is convenient. But unnecessary startup can slow a suite, and shared mutable data can make tests interfere. Choose the resource's lifetime before registering it.

Registration alone does not decide a lifetime. This lesson helps you choose an owner and a lifetime.

## Do it

### 1. Ask two questions

For any piece, ask who shares it and how long it lives. The answers pick the placement:

| Who shares it | How long it lives | Placement | The sample's piece | Who releases it |
| --- | --- | --- | --- | --- |
| Several tests use one instance | Until the run ends | run-owned resource | a broker container or loopback listener | the host |
| Each test needs its own instance | For one test | test-owned resource | a tenant or per-test WireMock server | the test's teardown |
| One test needs a local disposable | Within one operation | a local variable | a response held with `using` | the test code |
| One test uses a run-lived instance | Until the run ends | run-owned resource, if that lifetime is useful | a WireMock server registered with `PerRun()` | the host |
| Tests use an externally owned system | Managed outside the suite | configured address and client | a published application or existing broker | the external owner |

Choose the lifetime the resource needs, then register cleanup with that owner. Several tests can use the same registration while each receives a separate instance.

For an external system, choose whether the test needs the real service or a fake. The host can configure a client without owning the remote service's lifetime.

### 2. Spot the three traps

**Leaving ownership and evidence implicit.** A raw client or manually started container needs explicit cleanup and instrumentation. Test code can dispose it directly or register it as a test-owned resource.

The environment drill in the [failure tour](/learn/understand-failures/a-failure-tour) shows the tracing limit. It calls the API with a plain `HttpClient`, which ProtoTest does not record, so the trace has no `http.request` operation for it. The trace still records the test's duration and failure. Prefer the configured client when it provides the behavior you need.

**Choosing a longer lifetime without a reason.** WireMock uses a separate server per test by default. `PerRun()` shares a server, its stubs and its request log until the run ends. The server starts when a test resolves it, not merely when the host registers it.

A run-lived fake can be useful even if only one journey uses it. Choose that lifetime deliberately, and account for shared stubs and request history if other tests use it later.

**Sharing mutable seed data.** Tests can interfere when they update or delete the same seeded records. Prefer per-test data for mutable scenarios. Shared reference data can work when setup is explicit and tests leave it unchanged.

### 3. See the third trap in a trace

Open [l0-state-drill.prototrace](pathname:///lessons/l0-state-drill.prototrace) and [l0-state-fix.prototrace](pathname:///lessons/l0-state-fix.prototrace) in the [viewer](https://trace.prototest.dev). The drill assumes a project exists and receives 404. The fix creates a project in its test-owned tenant before reading it.

The drill shows an unmet data assumption. The fix shows explicit setup.

## What happened

Two answers decided where each piece went: who shares it, and how long it must live. A tenant used by one test belongs to that test, and its teardown removes it. A broker used by every test belongs to the host, which releases it after the last test. A response held for one call is a local variable you dispose yourself.

The trace sees only what goes through ProtoTest's clients. A raw `HttpClient` works, but it leaves no operation behind.

When several tests need the same setup behavior, a hook, attribute, client or integration can reuse it through the existing lifecycle.

## Check yourself

<Checkpoint
  question="A run hook seeds five projects. Several tests update and delete those same projects. What can go wrong, and how would you isolate the tests?"
  verify={<>Compare <a href="pathname:///lessons/l0-state-drill.prototrace">l0-state-drill.prototrace</a> with <a href="pathname:///lessons/l0-state-fix.prototrace">l0-state-fix.prototrace</a> in the <a href="https://trace.prototest.dev">viewer</a>.</>}>

One test can change or remove a record another test still needs. Give each mutable scenario its own tenant or records, and register their cleanup. Shared data is a different choice when tests only read it and setup guarantees its contents.

</Checkpoint>

## Remember

- Choose lifetime, sharing and cleanup ownership explicitly.
- Raw resources need cleanup and instrumentation even when a test owns them.
- Isolate mutable test data. Define shared reference data deliberately.

## Go deeper

- [Recipes](/docs/recipes/overview): common journeys with the composed client, from REST to GraphQL to a workbook.
- [Extending ProtoTest](/docs/advanced/extending): when the right answer is your own hook, attribute or integration.
- [The foundation](/docs/foundation/overview): the lifecycle those extensions plug into.
