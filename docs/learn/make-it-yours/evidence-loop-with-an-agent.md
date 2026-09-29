---
id: evidence-loop-with-an-agent
title: Run the evidence loop with an agent
sidebar_label: The evidence loop with an agent
sidebar_position: 5
description: "Read a real failing trace with the CLI, connect a coding agent to the same archive over MCP, and close the loop."
---

import LearnShell from '@site/src/components/LearnShell';
import Link from '@docusaurus/Link';

# Run the evidence loop with an agent

The four drills leave failing traces on purpose. Level 4 read one as a human. This lesson reads the same archive with the tools a coding agent uses, and the two must tell one story.

<LearnShell
  level="Level 6, lesson 5"
  minutes="About 9 minutes"
  outcome={[
    'Run the deterministic summary on a real failing trace.',
    'Connect a coding agent to the same archive over MCP and ask for the failure.',
    'Close the loop: fix, verify against a baseline, post the digest.',
  ]}
  before={[
    <>Write an integration (<Link to="/learn/make-it-yours/write-an-integration">lesson 3</Link>).</>,
    'The .NET SDK, and a coding agent that speaks MCP for the agent half.',
  ]}
  situation={
    <>
      <p>The evidence loop is fail, evidence, fix, verify, report. One file carries the evidence, and every step reads that same file, so the log, the agent and the pull request cannot disagree.</p>
      <p>The drill archive is the right test case: a failure that was recorded once and did not change afterwards.</p>
    </>
  }
  checkpoint={{
    question:
      'The agent asks for get_diagnosis with detail=context. Name two things it adds over the one-line summary, and one thing it can never do.',
    verify: (
      <>
        Read the diagnosis page's context package list, then ask your agent for the same drill and compare its answer with the summary output below.
      </>
    ),
    reveal: (
      <>
        The context package adds the failing operation's ancestor chain, its attributes, the source snippet when the archive embedded it, the artifacts it can reach and the state it changed. It never guesses: the rules read recorded evidence only, and a failure whose evidence was not recorded stays unexplained.
      </>
    ),
  }}
  learned={[
    'prototest summary and the MCP tools read one archive and select the same failure.',
    'A coding agent names the file, the line, the mismatch and the artifacts without reading the test output.',
    'The loop closes with verify against a baseline and feedback to the pull request.',
  ]}
  next={[
    {
      label: 'The evidence loop',
      to: '/docs/agent-workflows/loop',
      note: 'The action, the digest and what the reviewer sees.',
    },
    {
      label: 'Back to the curriculum',
      to: '/learn/',
      note: 'All seven levels, and where each lesson sits.',
    },
  ]}>

## Read the trace without an agent

Install the CLI, download the drill archive from <Link to="/learn/evidence/read-a-failing-trace">the failure lesson</Link>, and point the command at it:

```bash
dotnet tool install --global ProtoTest.Cli
prototest summary l0-time-drill.prototrace
```

The command reads the archive and prints one deterministic document. Here is its output for the committed drill, with the run id and timestamps shortened:

```text
ProtoTest trace 2.0 · run 316f2b23... · 2026-09-29 06:18:29Z - 2026-09-29 06:18:34Z
1 tests · 1 failed

FAILED Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow (2.16 s)
  Shape mismatch failed with 1 error(s):
    • [$.status]: Values did not match. (Expected: "past_due", Actual: "active")
  at samples/Northstar.ProtoTest/FailureDrills.cs:34 (Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow)
  assert.json.shape Assert response shape · failed
  cause: assertion (1 mismatch)
  mismatch: $.status: expected past_due, actual active
```

The document reads top to bottom:

- The failing test, with its runner name and duration. The duration is the recording's own; a rerun writes its own and changes nothing else.
- The recorded error and the source location, straight from the failure.
- The selected failing operation, `assert.json.shape`, which is the deepest failing check.
- The cause rule: an assertion, with the mismatch list. The mismatch names the JSON path and both values.

Download the archive and run the command yourself:

<a href="pathname:///lessons/l0-time-drill.prototrace">l0-time-drill.prototrace</a>

## Connect the agent to the same file

The MCP server is a .NET tool. Install it and register it with your client:

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

The server discovers the `.prototrace` archives under the folder. For one downloaded archive, replace the arguments with `["--trace", "<absolute path to l0-time-drill.prototrace>"]`. The [setup page](/docs/agent-workflows/setup) has the same shape for VS Code, Cursor and Claude Desktop, and the copy-in skill that teaches the loop.

The server exposes four read-only tools:

| Tool | Returns |
| --- | --- |
| `list_runs` | the newest runs first, with outcome counts and the failing test ids |
| `get_failure` | one failure: outcome, error, source location, the selected operation, mismatches and artifacts |
| `get_diagnosis` | the run's diagnosis, or one failure's context package with `detail: context` |
| `get_coverage` | coverage totals and uncovered units from the report the run embedded |

Ask the agent to list the runs, then for the failure in the time drill. It answers from the archive, and the failure selector is the same one the viewer and the summary use, so the names, the line and the mismatch come out identical.

With `detail: context`, the same failure returns its context package beside the summary:

```text
context: Northstar.ProtoTest.FailureDrills.ARealWaitDoesNotCloseTheDueWindow
ancestors: test.execution > http.request REST GET /api/v1/organization > assert.json.shape
attributes: $.status, expected past_due, actual active; clock unmoved, elapsed 1.33 s
source: samples/Northstar.ProtoTest/FailureDrills.cs:34
artifacts: rest-01-response, rest-01-expected-shape, scenario-summary.json
```

The five lines are the ancestor chain, the attributes, the source snippet location, the artifacts it can reach and the state it changed. The checkpoint at the top of this lesson asks what the package adds over the one-line summary, and what it can never do.

## Close the loop

A fix is not verified by the fix. Compare the new report with the report from before the change:

```bash
dotnet test
prototest verify baseline.json TestResults/ProtoTest/report.json
```

Then post the digest, locally or from CI:

```bash
prototest feedback TestResults/ProtoTest/run.prototrace --digest digest.json
```

The annotations go to stdout and the per-channel outcomes to stderr. With no target configured, the network channels skip with their reason, so a local run is safe. In CI, the feedback action uploads the trace, posts the comment and runs the verdict with the two reports; the [loop page](/docs/agent-workflows/loop) carries the workflow.

## What the agent reads, and what it does not

- It reads evidence that already exists. A run with no `.prototrace` is invisible. Write the trace first.
- It is read-only: no suite runs, no file changes, and nothing leaves the machine by default.
- Every payload is capped, and an archive opened from a stream has no file to read, so its sources and artifact contents are absent with that reason.
- The rules never guess. A failure whose evidence was not recorded is reported as unexplained, with a pointer to the viewer.

The suite writes one archive. The human and the agent read the same file. Verify the fix against the earlier report.

</LearnShell>
