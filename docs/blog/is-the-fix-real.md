---
slug: is-the-fix-real
date: 2026-10-02
draft: true
image: /img/blog/is-the-fix-real.png
title: How do you know your agent's fix is real?
description: A coding agent can make a red test green in ways that fix nothing. Here is how ProtoTest decides when a fix is proven.
authors: [mseys]
tags: [ai, agents, mcp, testing]
---

Ask a coding agent to fix a failing integration test and it will usually come back with a green run. The question
is what made it green.

{/* truncate */}

## Green is not the same as fixed

I have watched agents, and people under time pressure, turn a red test green in all of these ways:

- **Loosen the check.** The expected status becomes "any 2xx". The expected shape loses the field that was wrong.
- **Stop checking.** The assertion is commented out, or the call stays and nothing reads its answer.
- **Wait it out.** A `Task.Delay(2000)` before the call, so the race still exists but usually loses.
- **Fix one, break another.** The test that was red passes now. A test that was green fails now, and nobody
  reran the whole suite.
- **Get lucky.** The test was flaky. It passed this time.

Every one of these ends with a green test and a confident message. None of them is a fix. And the more code an
agent writes, the less of it a person reads closely, so "the tests pass" carries more weight than it can bear.

## Make the evidence decide

ProtoTest already records every test run as a trace: each request and response, each check with what it expected
and what it got, the state the test saw, and the files it produced. So instead of asking the agent whether it is
done, we can ask the evidence.

`prototest prove` takes the run that showed the failure and one or more runs after the change:

```bash
prototest prove before.prototrace after.prototrace
```

It calls the fix proven only when all of these hold:

- the baseline recorded each claimed test as not succeeded;
- every run after the change recorded each claimed test as succeeded;
- no test that passed in the baseline fails now;
- when both runs embedded a JSON report, comparing the reports finds nothing failing.

That covers three of the five failure modes directly. A fix that breaks another test is not proven. A test that
was not failing to begin with cannot be "fixed". And a flaky pass is caught by passing two or more runs after the
change: each one has to pass.

When it is proven, it says where each test changed:

```text
ProtoTest fix proven: baseline run 787b2408… -> run c699dde1…
  PROVEN ProjectsJourney.CreatingAProjectReturnsIt
    changed at: assert.json.shape [POST /api/v1/projects]
  PROVEN PlatformJourney.RestWritesAreVisibleThroughGraphQL
    changed at: assert.json.shape [query Projects]
```

When it is not, each unmet condition gets a reason code (`not-failing-in-baseline`, `still-failing`, and so on)
naming the run it is about, and the command exits `1`, so a pipeline can stop on it.

## A green test can still prove little

The other two failure modes, a check that was loosened or removed and a sleep that hides a race, leave the test
green and the suite green. Comparing runs will not find them. Reading what the test recorded will.

`prototest review` reads each test's trace and reports three things:

| Rule | What was recorded |
| --- | --- |
| `no-check` | the test body ran operations and no check |
| `unchecked-call` | a call that no check looked at before the next call |
| `untraced-gap` | 250 ms or more of the test body with no operation, usually a sleep |

Every finding comes with its next step: assert on the answer, check what the call returned, or replace the sleep
with a wait that records what it waits for. A review advises; it does not fail the build.

## Where the agent comes in

None of this needs an agent. The same commands work for a person. But they were built so that an agent can use
them in its own loop, through ProtoTest's MCP server:

1. `get_failure` gives the error and the line.
2. `compare_runs` shows the operation where this run left the last green one.
3. The agent changes the code or the test, and reruns.
4. `check_fix` returns the receipt: proven, or each reason it is not.

The agent does not decide when it is done. The receipt does. A new project from `dotnet new prototest` is set up
for this from the start, with an `AGENTS.md`, a `.mcp.json` and the skills that describe the loop.

On a pull request, the same comparison runs against the base branch. One comment says which tests the change
broke or fixed and where, and which new endpoint no test calls yet, with an existing test to start from. The
comment is updated on every push instead of piling up.

## What it does not do

To be honest about the limits:

- **It does not write your code.** It judges what changed. Which agent you use is up to you.
- **It only knows what was recorded.** A check outside ProtoTest's clients and assertions is not in the trace, so
  it cannot be reviewed.
- **It is a preview.** The agent layer is new in 1.1, and its surface can still change before 1.2, based on what
  people run into.

If your team is letting agents touch integration tests, I would like to hear how it goes. The
[coding agents guide](/docs/agent-workflows/coding-agents) has the setup, and
[the CLI reference](/docs/agent-workflows/cli) has every command.
