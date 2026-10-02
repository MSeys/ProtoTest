---
id: evidence-loop-with-an-agent
title: Run the evidence loop with an agent
sidebar_label: The evidence loop with an agent
sidebar_position: 6
description: "Summarize a failing trace with the CLI, connect a coding agent to the same file over MCP, and let ProtoTest judge the fix."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# Run the evidence loop with an agent

<Lesson
  track="Extend ProtoTest"
  step="Lesson 6 of 6"
  minutes={9}
  outcomes={[
    'Summarize a real failing trace from the command line',
    'Connect a coding agent to the same file over MCP and ask for the failure',
    'Let ProtoTest review a test and prove a fix',
  ]}
  needs={[
    <>The previous lesson, <a href="/learn/extend/swap-a-dependency-for-one-test">Swap a dependency for one test</a></>,
    'The .NET SDK, and a coding agent that speaks MCP for the second half',
  ]}
/>

## The problem

A test fails in CI. A person reads the log, a coding agent guesses from the output, and the pull request carries a third account of what happened. The three can disagree.

The evidence loop avoids that: fail, evidence, fix, verify, report. One `.prototrace` file carries the evidence, and every step reads that same file. This lesson reads one failing trace first with a command, then with an agent.

## Do it

### 1. Summarize the trace with the CLI

Install the CLI and download the time drill archive from <Link to="/learn/understand-failures/read-a-failing-trace">the failure lesson</Link>: <a href="pathname:///lessons/l0-time-drill.prototrace">l0-time-drill.prototrace</a>. Then run:

```bash
dotnet tool install --global ProtoTest.Cli
prototest summary l0-time-drill.prototrace
```

The command prints one deterministic document. This is the drill's output, with the run id shortened:

```text
ProtoTest trace 2.0 · run d7b73deb... · 2026-09-29 18:36:43Z - 2026-09-29 18:36:46Z
1 tests · 1 failed

FAILED Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow (1.79 s)
  Shape mismatch failed with 1 error(s):
    • [$.status]: Values did not match. (Expected: "past_due", Actual: "active")
  at samples/Northstar.ProtoTest/FailureDrills.cs:36 (Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow)
  assert.json.shape Assert response shape · failed
  cause: assertion (1 mismatch)
  mismatch: $.status: expected past_due, actual active
```

It names the failing test, the error with its source line, the deepest failing check (`assert.json.shape`) and the cause: one JSON path with both values.

### 2. Connect an agent to the same file

MCP, the Model Context Protocol, is how a coding agent calls tools. ProtoTest's MCP server is a .NET tool that lets the agent read traces. Install it:

```bash
dotnet tool install --global ProtoTest.Mcp
```

A project-scoped client reads `.mcp.json` at the repository root:

```json
{
  "mcpServers": {
    "prototest": {
      "command": "prototest-mcp",
      "args": ["--project", "."]
    }
  }
}
```

The server finds the `.prototrace` archives under that folder. For one downloaded archive, use `["--trace", "<absolute path to l0-time-drill.prototrace>"]` as the arguments instead. The [setup page](/docs/agent-workflows/setup) has the same shape for VS Code, Cursor and Claude Desktop.

### 3. Ask the agent about the failure

The server offers read-only tools. These four read a failure:

| Tool | Returns |
| --- | --- |
| `list_runs` | the newest runs, with counts and failing test ids |
| `get_failure` | one failure: error, source line, failing check, mismatches, artifacts |
| `get_diagnosis` | the run's diagnosis, or one failure's context with `detail: context` |
| `get_coverage` | coverage totals, uncovered units, and the test to start from for each |

Ask the agent to list the runs, then for the failure in the time drill. The names, the line and the mismatch come out identical to the summary, because the viewer, the summary and the tools select the failure the same way.

With `detail: context` the failure also returns its context package. This is the drill's:

```text
context: Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow
ancestors: test.execution > http.request REST GET /api/v1/organization > assert.json.shape
attributes: $.status, expected past_due, actual active
source: samples/Northstar.ProtoTest/FailureDrills.cs:36
artifacts: rest-01-response, rest-01-expected-shape, scenario-summary.json
```



### 4. Let ProtoTest judge

The agent does the fixing. ProtoTest decides whether the result holds. Review the drill:

```bash
prototest review l0-time-drill.prototrace
```

```text
untraced-gap: 1.0 s of the test body recorded no operation, starting 154 ms in, before 'REST · GET /api/v1/organization'.
  next: Replace a sleep with a wait that records what it waits for (ProtoPolling, a message await, the test clock), ...
```

The drill sleeps for real. Its fixed version, <a href="pathname:///lessons/l0-time-fix.prototrace">l0-time-fix.prototrace</a>, moves the test clock instead, and `prototest review` reads it clean.

For your own fix, keep the trace of the failing run and the one after the fix, then ask for the receipt:

```bash
prototest prove before.prototrace after.prototrace
```

It says proven only when the test failed before, passes now, and no other test broke. Over MCP they are `review_tests` and `check_fix`.

## What happened

The suite wrote one archive. The command and the agent both read that archive, so they could not tell different stories. The agent did not read the test output at all. It named the file, the line, the mismatch and the artifacts from recorded evidence.

The tools are read-only: they run no suite and change no file. They never guess either. A failure whose evidence was not recorded is reported as unexplained, and a fix counts only when the receipt proves it.

## Check yourself

<Checkpoint
  question="The agent asks for get_diagnosis with detail=context. Name two things it adds over the one-line summary, and one thing it can never do."
  verify={<>Read the diagnosis page's context package list, then ask your agent for the same drill and compare its answer with the summary output above.</>}>

It adds the failing check's ancestor chain and the artifacts the agent can reach (also its attributes and source). It can never guess: a failure without recorded evidence stays unexplained.

</Checkpoint>

## Remember

- `prototest summary` and the MCP tools read one archive and select the same failure.
- The loop is fail, evidence, fix, verify, report. ProtoTest judges the fix: `prove` and `check_fix`.
- The agent reads recorded evidence only, so the trace has to exist first.

Next: back to [all the tracks](/learn/).

## Go deeper

- [Coding agents](/docs/agent-workflows/coding-agents): the three jobs and the tool that judges each.
- [The evidence loop](/docs/agent-workflows/loop): the action, the digest and what the reviewer sees.
- [Agent setup](/docs/agent-workflows/setup): clients and the copy-in skill that teaches the loop.
