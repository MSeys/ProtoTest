---
sidebar_position: 2
title: Setup
description: Install the ProtoTest MCP server and register it with a coding agent so it can read the runs in your repository.
---

import TabbedCode from '@site/src/components/TabbedCode';

# Setup

One install connects a coding agent to the runs in your repository. The server is a .NET tool. It reads `.prototrace` archives and answers questions about them over the Model Context Protocol.

## Install

```bash
dotnet tool install --global ProtoTest.Mcp
```

The tool command is `prototest-mcp`. The package targets .NET 8, 9 and 10, so the install picks the framework your SDK has.

## Register it

An MCP client starts the server and talks to it over stdio. Add the server to the client's configuration file. The shape is the same everywhere; only the file and the root key change.

<TabbedCode
  label="Client configuration"
  tabs={[
    {
      id: 'claude-code',
      label: 'Claude Code',
      filename: '.mcp.json (repository root)',
      code: `{
  "mcpServers": {
    "prototest": {
      "command": "prototest-mcp",
      "args": ["--project", "."]
    }
  }
}`,
    },
    {
      id: 'vscode',
      label: 'VS Code',
      filename: '.vscode/mcp.json',
      code: `{
  "servers": {
    "prototest": {
      "type": "stdio",
      "command": "prototest-mcp",
      "args": ["--project", "."],
      "cwd": "\${workspaceFolder}"
    }
  }
}`,
    },
    {
      id: 'cursor',
      label: 'Cursor',
      filename: '.cursor/mcp.json',
      code: `{
  "mcpServers": {
    "prototest": {
      "command": "prototest-mcp",
      "args": ["--project", "."]
    }
  }
}`,
    },
    {
      id: 'claude-desktop',
      label: 'Claude Desktop',
      filename: 'claude_desktop_config.json (user settings)',
      code: `{
  "mcpServers": {
    "prototest": {
      "command": "prototest-mcp",
      "args": ["--project", "C:\\\\dev\\\\your-repo"]
    }
  }
}`,
      footnote: 'Its working directory is not your repository, so give --project an absolute path.',
    },
  ]}
/>

The server resolves `.` against its working directory. Keep the file at the repository root. If the client starts the server elsewhere, pass an absolute `--project` path.

## Check it

Three steps, none of which needs a suite of your own.

1. Read a committed failing run with the CLI. Download [l0-environment-drill.prototrace](pathname:///lessons/l0-environment-drill.prototrace) from the Learn track, then:

   ```bash
   dotnet tool install --global ProtoTest.Cli
   prototest summary l0-environment-drill.prototrace
   ```

   ```text
   ProtoTest trace 2.0 · run 761778e6dc82498a9f9965fa1e6b5a24 · 2026-09-29 06:19:01Z - 2026-09-29 06:19:05Z
   1 tests · 1 failed

   FAILED Northstar.ProtoTest.FailureDrills.TheAddressWasHardcodedForOneMachine (2.66 s)
     ConnectionError reaching http://127.0.0.1:5099: connection refused.
     test.execution Test execution · failed
     cause: runner-reported failure
   ```

   That is a failing run of the Learning demo, so the summary prints the run, the failing test and the failing operation. `prototest summary` prints the same diagnosis the MCP tools return, which makes it the quickest way to check that the file an agent would read says what you expect. The [CLI reference](./cli.md) documents all four verbs, their arguments and their exit codes.

2. Point the server at one file with `--trace`, or at a folder of runs with `--project`. For one archive, `--trace` works wherever the file was written.

3. Run your own suite once so a `.prototrace` exists, and ask the agent to list the runs. It should answer with a run id and a trace file from your machine.

## Where it reads

```mermaid
flowchart TD
    start{"How did the server start?"}
    start -->|"--trace file"| one["Reads exactly that archive<br/>a folder argument is refused"]
    start -->|"--project folder"| pr{"TestResults/ holds a readable trace?"}
    start -->|"neither"| env["PROTOTEST_PROJECT, else the working directory"]
    env --> pr
    pr -->|"yes"| stop["Use it and stop there"]
    pr -->|"no"| walk["Walk the tree, skipping<br/>bin, obj, .git, node_modules"]
```

"Newest" is the run's recorded start time, never a file timestamp. An archive the reader cannot open is skipped with a reason in `list_runs` and never guessed at.

The default trace lands below the test project's build output, and the walk skips `bin`, so the server cannot see it from the repository root. Give the suite a results folder the server can see. The [continuous integration page](../continuous-integration/index.md#put-every-artifact-in-one-place) sets that up with one environment variable, so CI and local runs write to the same place:

```csharp
var results = Environment.GetEnvironmentVariable("PROTOTEST_RESULTS")
    ?? Path.Combine("TestResults", "ProtoTest");

builder.ConfigureTracing(trace =>
    trace.OutputPath = Path.Combine(results, "run.prototrace"));
```

Set `PROTOTEST_RESULTS` to an absolute path such as `<repository>/TestResults` when you run locally.

## Give the agent the skill

The repository carries one skill that teaches the evidence loop and the four tools: [`skills/prototest-evidence-loop/SKILL.md`](https://github.com/MSeys/ProtoTest/blob/main/skills/prototest-evidence-loop/SKILL.md). It is copy-in, not an install.

A client that reads skills folders (Claude Code, for example) loads it from its skills directory. From a checkout of the ProtoTest repository:

```bash
mkdir -p .claude/skills
cp -r skills/prototest-evidence-loop .claude/skills/
```

Otherwise download the file from the repository and place it in the same layout. A client without a skills folder reads the file as an instruction instead: paste its body into the client's rules or instructions file.

The skill is optional. The MCP server's tool descriptions are the contract, so an agent without the bundle can still list the tools and work from them.

## What the agent can see

Every tool is read-only and returns a compact JSON document. `list_runs` over a folder holding one failing run:

```json
{
  "root": "C:/dev/your-repo",
  "runs": [
    {
      "runId": "761778e6dc82498a9f9965fa1e6b5a24",
      "traceFile": "C:/dev/your-repo/TestResults/run.prototrace",
      "startedAtUtc": "2026-09-29T06:19:01Z",
      "completedAtUtc": "2026-09-29T06:19:05Z",
      "outcomes": { "failed": 1 },
      "failingTests": [ { "testId": "00001", "name": "TheAddressWasHardcodedForOneMachine" } ],
      "failingTestsTruncated": false
    }
  ],
  "truncated": false,
  "skipped": null
}
```

| Tool | Input | Returns | Cap |
| --- | --- | --- | --- |
| `list_runs` | optional `folder`, `limit` (default 10) | the newest runs first: run id, trace file, start and completion, outcome counts, failing test ids, and the archives it had to skip | 50 runs |
| `get_failure` | optional `runId`, `testId` | the failure entry: outcome, error, source location, the selected failing operation, the shape mismatches and the test's artifacts | 10 failed operations, 25 mismatches |
| `get_diagnosis` | optional `runId`, `testId`, `detail` (`summary` or `context`) | the run's diagnosis, or one failing test's context package | the [diagnosis caps](./diagnosis.md#limits) |
| `get_coverage` | optional `runId`, `target`, `category`, `includeUncovered`, `offset`, `limit` | coverage totals and uncovered units from the report the run embedded | 200 uncovered units |

[Diagnosis](./diagnosis.md) explains what `get_failure` and `get_diagnosis` return and what the agent can do with it.

## Limits

- Read-only: no trace is written, no suite is rerun, the stdio host binds no port, and nothing on disk is modified.
- Nothing leaves the machine by default: no telemetry, no uploads, no accounts. The stdio host reads local archives, stdout carries the protocol only, and logs go to stderr.
- Hard caps bound every payload. `list_runs` returns at most 50 runs, `get_coverage` at most 200 uncovered units, and `get_diagnosis` applies the [diagnosis caps](./diagnosis.md#limits).
- The server reads evidence that already exists. A run with no `.prototrace` is not visible to it; write the trace first.
- One external dependency: the official `ModelContextProtocol` SDK (Apache-2.0).
- The demo endpoint is a local sample. See [Coding agents](./coding-agents.md#demo-endpoint).
