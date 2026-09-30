---
sidebar_position: 1
title: Why I built ProtoTest
description: The problems and earlier experience that led to ProtoTest.
---

# Why I built ProtoTest

Every integration was its own island. ProtoTest gives them one host, one context, one trace.

I love programming, but especially building tools and solving abstract problems. I can build features, but problem-solving is like a puzzle.

Integration testing gave me plenty of those problems.

## Tests stopped being about the test

Integration testing gets hard for larger applications, such as multi-tenant services. There is infrastructure to start, users and tenants to create, authentication to arrange, data to clean up and different services to talk to.

At some point more of the test is about that setup than the behavior it is meant to check. My focus has always been clean and readable code. I wanted common application setup outside the test, while setup that matters to the scenario should remain visible:

```csharp
// Before: the test builds the world, then checks one thing.
var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000") };
var token = await SignInAsTestUser(client);
var tenant = await CreateTenant(client, token);
var response = await client.PostAsync("/api/projects", content);

// After: the host and attributes built the world. The test is the scenario.
using var response = await Proto.Context.Rest()
    .Body(new { product = "notebook", quantity = 2 })
    .PostAsync("/api/orders");
response.Should.HaveHttpStatus(HttpStatusCode.Created);
```

## I had built a testing framework before

ProtoTest was not my first attempt at this.

I had already built a similar testing framework from scratch. It started around API testing and grew as we looked at which other parts of an application could be covered.

I learned a lot from building and using it. It covered less and its core was harder to reuse. Building and using it taught me what to change.

ProtoTest was built from scratch, but the vision did not start from scratch.

## Why one foundation?

There are already good libraries for HTTP, browsers, containers and most other things ProtoTest works with. I did not build ProtoTest to replace them.

Each integration handled setup, auth and diagnostics on its own. Setup, authentication, cleanup and diagnostics were handled differently or had to be connected by the test project. ProtoTest gives those integrations the same host, test context and lifecycle. They can use setup that already happened and write their operations to the same trace. The [comparison](./compare.md) counts the plumbing per fixture: 35 lines without, 13 with.

The integrations are still opinionated wrappers. They represent how I want to write tests with the libraries underneath them. That will not be the best choice for everyone.

## Why tracing?

Moving common setup outside a test makes the scenario easier to read, but it can also hide what happened before the test method ran.

That hurts when a test fails only in CI, fails intermittently, or needs several infrastructure pieces. A failed assertion is often only the final part of the story. ProtoTrace exists to show the lifecycle around that failure: setup, operations, checks, cleanup and captured evidence from every integration that took part. [Open the sample trace](https://trace.prototest.dev/?demo=1) to see one: a run of the sample suite, with four failing tests and one partial one. The file stays readable to readers from its own era; the [compatibility matrix](../observability/prototrace-archive.md#format-compatibility) states which reader opens which version.

Playwright tracing was a large inspiration, but I wanted the trace to cover more than browser actions.

## Stability you can adopt on

ProtoTest 1.x stays additive. Released APIs change only through deprecated shims, and nothing breaks without a plan decision recorded in the changelog. Most packages are supported; a smaller preview set (the agent layer, devices, Sheets, Aspire, WireMock, MassTransit and analyzers) can still change before 1.2. Support is best effort by one maintainer in personal time; the [sustainability page](./sustainability.md) has the full story. If the foundation stops fitting, [Leaving ProtoTest](./leaving.md) removes it one test at a time.

## Nothing phones home

ProtoTest runs entirely in your process.

| Telemetry | Account | Viewer | Attachments |
| --- | --- | --- | --- |
| None | None | A static page that reads the file in your browser | Redacted and sanitized before they are written |

A `.prototrace` stays on your machine unless you move it. The [benchmarks page](./benchmarks.md) documents the size levers if a trace is too large to share.

## What I wanted to build

Tests should focus on the scenario. Integrations should share lifecycle and diagnostics. Failures should leave enough to investigate.

ProtoTest is my attempt to solve those problems.

Writing it meant writing a lot of code and documentation, and I used AI heavily to do it - how, why and with what reservations is on the [AI usage page](./ai-usage.md).

It is a new project and real use will show where I got things wrong. That is part of building it too.
