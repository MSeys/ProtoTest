---
slug: why-nobody-trusts-their-integration-tests
date: 2026-10-02T18:30
image: /img/blog/why-nobody-trusts-their-integration-tests.png
title: Why nobody trusts their integration tests
description: Integration tests catch the bugs that matter, and still end up ignored. Three complaints explain why, and what changes when the framework owns the hard parts.
authors: [mseys]
tags: [integration-testing, dotnet, testing]
---

import CommandBox from '@site/src/components/CommandBox';

Ask a .NET team about their unit tests and you hear numbers: coverage, speed, how many. Ask about their integration
tests and you hear complaints. Yet the integration tests are the ones that catch the bugs that matter: the API that
writes something the UI never shows, the message that arrives twice, the job that runs at the wrong time.

I kept hearing the same three complaints, and I kept having them myself. ProtoTest is my answer to all three.

{/* truncate */}

## "Every test is mostly setup"

An integration test needs a running application, a database with the right data, a signed-in user, clients with
the right addresses, and a cleanup that runs even when the test fails. Written by hand, that is dozens of lines
before the first line of behaviour. Copy it into the next test, and the next, and the suite becomes a pile of
setup with a few assertions hidden inside.

In ProtoTest the suite does that work once. A test asks for what it needs with attributes, and the body is only the
behaviour:

```csharp
[Application("Api")]
[ProtoTest]
[SignedInAs]
public async Task RestWritesAreVisibleThroughGraphQL()
{
    using var created = await Proto.Context.Rest()
        .Body(new CreateProjectRequest("atlas"))
        .PostAsync("/api/v1/projects");

    created.Should.HaveHttpStatus(HttpStatusCode.Created);

    using var projects = await Proto.Context.GraphQL()
        .Query("projects", new { first = 10 })
        .ExpectAsync(new { totalCount = 1 });
}
```

The application starts once for the suite. The user is created for this test and removed after it. The clients
know their addresses. Written as a regular NUnit fixture against the same application, a comparable test carries 35
lines of plumbing; with ProtoTest it carries 13. The [side-by-side comparison](/docs/project/compare) marks every
one.

## "It fails sometimes, and nobody knows why"

The flaky test. It passes on your machine and fails on CI, or fails on Monday and passes on retry. After a few weeks
people stop reading its failures, and then it stops protecting anything.

Almost every flaky integration test comes down to something the test did not own:

- **Time.** The test waits a real second and hopes the job has run. On a busy machine it has not.
- **State.** The test reads a record an earlier test created. In a different order, it is not there.
- **Addresses.** The test talks to `localhost:5099` because that is where it ran on one machine.

ProtoTest gives each test its own. Every test gets its own clock, which the application reads and the test moves:
`Proto.Context.Clock.Advance(TimeSpan.FromDays(8))` instead of a `Task.Delay`. State comes from attributes that
create it before the test and remove it after, even on failure, so tests can run in parallel. Addresses come from
one place, the suite's host, so the same test runs against an in-process API, a container or a staging URL without
a change.

## "When it fails, I spend an hour finding out why"

A test expects `201 Created` and gets `400`. The assertion says exactly that, and nothing else. The response body
that explained the problem is gone. So you rerun it with logging, set a breakpoint, and reconstruct what happened
before the assertion.

In ProtoTest every run records that for you. The trace holds each request and response, each check with what it
expected and what it got, the state the test saw and the files it produced, in the order they happened. A failed
check carries the response body in its message. The viewer and the HTML report open on what needs attention: what
failed, and why. You read the cause before you open the code.

[Open a sample run in the viewer](https://trace.prototest.dev/?demo=1) to see what that looks like.

## The same three, across every boundary

These complaints get worse as a test crosses more boundaries: an API, a message broker, a browser, a device on a
WebSocket. More setup, more things to own, more places for a failure to hide. ProtoTest keeps one model for all of
them. REST, GraphQL, gRPC, SQL, messaging, browsers and devices use the same attributes, the same clock, the same
host and one trace for the whole run.

## And now that agents write tests

More tests are now written and fixed by coding agents. That makes the third complaint more urgent: a green run
proves less than it seems, because a check can be loosened or a sleep can hide a race. ProtoTest reads the same
trace to judge an agent's work, and only calls a fix proven when the test failed before, passes now and nothing
else broke. That part is new in 1.1, and in preview.

## Try it

The template creates a suite with a first test. Run it and open the trace it writes:

<CommandBox
  title="A first suite in a minute"
  commands={['dotnet new install ProtoTest.Templates', 'dotnet new prototest -n Shop', 'cd Shop && dotnet test']}
/>

The [Learn track](/learn/) goes from that first test to a suite you trust. And if you want to know why 1.1 exists
at all, I wrote about [why 1.0 shipped too early](/blog/why-1-0-shipped-too-early).
