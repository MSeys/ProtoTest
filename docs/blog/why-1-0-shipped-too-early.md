---
slug: why-1-0-shipped-too-early
date: 2026-10-02T09:00
image: /img/blog/why-1-0-shipped-too-early.png
title: Why ProtoTest 1.0 shipped too early
description: What was wrong with 1.0, what I changed for 1.1, and the rules I follow now.
authors: [mseys]
tags: [prototest, release, open-source]
---

import CommandBox from '@site/src/components/CommandBox';

A few weeks after I released ProtoTest 1.0, I deleted the LinkedIn post that announced it. Nothing had crashed. No
one had complained. I deleted it because, reading my own docs again, I no longer agreed with them.

This post is about what was wrong, what I changed for 1.1, and the rules I work by now.

{/* truncate */}

## What 1.0 got right

The idea held up. Most of an integration test is not the test: it is starting the application, seeding data,
creating clients, finding addresses, cleaning up and collecting diagnostics. ProtoTest owns that part. A test asks
for what it needs through attributes and gets a context with everything wired, so the body is only the behaviour.

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

Written as a regular NUnit fixture against the same application, a comparable test carries 35 lines of plumbing.
With ProtoTest it carries 13. The [side-by-side comparison](/docs/project/compare) marks every one of them.

Every run also wrote a trace: the requests, the checks, the state, the files. That foundation is still the
foundation in 1.1.

## What was wrong

Three things, and I was the one who caused all of them.

**The docs promised more than the code did.** Some pages described behaviour as if it shipped, when it was planned
or only half there. For a testing tool that is the worst possible mistake. You choose a test framework because you
trust it to tell you the truth about your code. If its own documentation is not true, why would you trust its
results?

**Every integration felt a little different.** REST, gRPC, messaging, SQL and the browser had grown one at a time,
and it showed. Options were registered in different ways. Waits were written in different ways. You learned
ProtoTest once per package instead of once.

**The evidence was there, but you had to dig for it.** The trace recorded everything, which is not the same as
explaining anything. A failing run gave you a lot of data and very little answer.

## What I did about it

I spent the last weeks on nothing else. Not on new ideas, but on making what existed true.

**Every docs page was checked against the code.** Where the code did less than the page, either the code changed
or the page did. The integration pages have a Limits section that says what they do not do. A Learn
track in seven parts walks from a first test to extending the framework.

**One model for every package.** There is one way to register options, one way to wait, one way to declare a
capability and one way to register infrastructure. A capability is only declared by something that can serve it:
when an integration has no address, it goes inert and its capability is absent. A test marked with
`[RequiresCapability]` is then skipped with the reason instead of failing on a connection error.

**Evidence that leads with the answer.** The viewer and the HTML report now open on what needs attention: what
failed, the rule that explains it, the mismatches. You read the cause before you open a single test.

Doing this meant breaking things. 1.1 removes and renames some 1.0 API, and I am not hiding that: the
[migration guide](/docs/getting-started/migrating-from-1-0) lists every change and what to use instead. From 1.1
on, the published 1.x surface only grows. Nothing you use will be removed in a 1.x release.

## The rules I follow now

These are written down in the repository, and every change goes through them:

1. **Honest capabilities, honest docs.** Never advertise what does not exist, not in the docs, the README or a post
   like this one.
2. **One behaviour change, one test.** Including the failure paths: a missing address, a timeout, a cancellation, a
   teardown that fails.
3. **Evidence or it did not happen.** A change is done when the full gate is green: tests, lint and the docs check.
4. **Never a second mechanism.** If the framework already owns something, extend it. Do not build a parallel one.

The second rule paid off while I was preparing this release. The Windows build on CI kept failing on a test that
always passed on my machine. The cause was a real bug in the shared polling loop: when less than a millisecond was
left before a deadline, the wait rounded down to zero and the loop spun until the deadline passed. Windows timers
are coarse enough to hit that window. It is fixed, with a test that failed before the fix.

## What 1.1 adds

With the foundation in order, 1.1 also adds new things:

- **Devices** over WebSocket, MQTT, TCP and serial lines, in the same test as your API and browser.
- **Aspire, WireMock, Testcontainers and hosted workers** as parts of the same run.
- **An agent layer, in preview**: an MCP server and a CLI that read the trace, compare two runs, and decide whether
  a fix is proven. The [coding agents guide](/docs/agent-workflows/coding-agents) shows the loop.

46 packages, one version, one model.

## Try it

<CommandBox
  title="A first suite in a minute"
  commands={['dotnet new install ProtoTest.Templates', 'dotnet new prototest -n Shop', 'cd Shop && dotnet test']}
/>

If you tried 1.0 and walked away, I understand. I would ask you to look again. And if something in 1.1 does not
do what the docs say, that is a bug, and I want to hear about it on
[GitHub](https://github.com/MSeys/ProtoTest/issues).
