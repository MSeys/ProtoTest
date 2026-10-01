---
id: evidence-loop-with-an-agent
title: Run the evidence loop with an agent
sidebar_label: The evidence loop with an agent
sidebar_position: 5
description: "Summarize a failing trace with the CLI, connect a coding agent to the same file over MCP, and close the loop with a verified fix."
---

import Lesson from '@site/src/components/Lesson';
import Checkpoint from '@site/src/components/Checkpoint';
import Link from '@docusaurus/Link';

# Run the evidence loop with an agent

<Lesson
  track="Extend ProtoTest"
  step="Lesson 5 of 5"
  minutes={9}
  outcomes={[
    'Summarize a real failing trace from the command line',
    'Connect a coding agent to the same file over MCP and ask for the failure',
    'Verify a fix against a baseline and post the digest',
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

Read it top to bottom:

- The failing test, with its duration from the recording.
- The recorded error and the source location.
- The selected failing operation, `assert.json.shape`: the deepest check that failed.
- The cause: an assertion, with the JSON path and both values.

### 2. Connect an agent to the same file

The MCP server is a .NET tool. Install it:

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

The server offers four read-only tools:

| Tool | Returns |
| --- | --- |
| `list_runs` | Newest runs first, with outcome counts and the failing test ids. |
| `get_failure` | One failure: outcome, error, source location, selected operation, mismatches and artifacts. |
| `get_diagnosis` | The run's diagnosis, or one failure's context package with `detail: context`. |
| `get_coverage` | Coverage totals and uncovered units from the report the run embedded. |

Ask the agent to list the runs, then for the failure in the time drill. The names, the line and the mismatch come out identical to the summary, because the viewer, the summary and the tools select the failure the same way.

With `detail: context` the failure also returns its context package. This is the drill's:

```text
context: Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow
ancestors: test.execution > http.request REST GET /api/v1/organization > assert.json.shape
attributes: $.status, expected past_due, actual active
source: samples/Northstar.ProtoTest/FailureDrills.cs:36
artifacts: rest-01-response, rest-01-expected-shape, scenario-summary.json
```

The five lines are the test, the ancestor chain, the mismatch attributes, the source location and the artifacts the agent can reach.

### 4. Close the loop

A fix does not verify itself. After the change, compare the new report with the report from before it:

```bash
dotnet test
prototest verify baseline.json TestResults/ProtoTest/report.json
```

Then post the digest, locally or from CI:

```bash
prototest feedback TestResults/ProtoTest/run.prototrace --digest digest.json
```

The annotations go to stdout and the per-channel outcomes to stderr. With no target configured, the network channels skip with their reason, so a local run is safe. In CI, the feedback action uploads the trace, posts the comment and runs the verdict with the two reports. The [loop page](/docs/agent-workflows/loop) carries the workflow.

## What happened

The suite wrote one archive. The command and the agent both read that archive, so they could not tell different stories. The agent did not read the test output at all. It named the file, the line, the mismatch and the artifacts from recorded evidence.

Its limits are part of the design:

- It reads evidence that already exists. A run with no `.prototrace` is invisible, so write the trace first.
- It is read-only: no suite runs, no file changes, and nothing leaves the machine by default.
- Every payload is capped. An archive opened from a stream has no file to read, so its sources and artifact contents are absent, with that reason.
- It never guesses. A failure whose evidence was not recorded is reported as unexplained, with a pointer to the viewer.

## Check yourself

<Checkpoint
  question="The agent asks for get_diagnosis with detail=context. Name two things it adds over the one-line summary, and one thing it can never do."
  verify={<>Read the diagnosis page's context package list, then ask your agent for the same drill and compare its answer with the summary output above.</>}>

The context package adds the failing operation's ancestor chain, its attributes, the source snippet when the archive embedded it, the artifacts it can reach and the state it changed. It never guesses: the rules read recorded evidence only, and a failure whose evidence was not recorded stays unexplained.

</Checkpoint>

## Remember

- `prototest summary` and the MCP tools read one archive and select the same failure.
- The loop is fail, evidence, fix, verify, report. Verify the fix against an earlier report.
- The agent reads recorded evidence only, so the trace has to exist first.

Next: back to [all the tracks](/learn/).

## Go deeper

- [The evidence loop](/docs/agent-workflows/loop): the action, the digest and what the reviewer sees.
- [Agent setup](/docs/agent-workflows/setup): clients and the copy-in skill that teaches the loop.
