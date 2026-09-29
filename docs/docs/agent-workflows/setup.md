---
sidebar_position: 2
title: Setup
description: Install the ProtoTest MCP server and register it with a coding agent so it can read the runs in your repository.
---

# Setup

One install connects a coding agent to the runs in your repository. The server is a .NET tool. It reads `.prototrace` archives and answers questions about them over the Model Context Protocol.

## Install

```bash
dotnet tool install --global ProtoTest.Mcp
```

The tool command is `prototest-mcp`. The package targets .NET 8, 9 and 10, so the install picks the framework your SDK has.

## Register it

An MCP client starts the server and talks to it over stdio. Add the server to the client's configuration file. For a project-scoped client, the file is `.mcp.json` at the repository root:

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

The server resolves `.` against its own working directory, which the client sets. Keep the file at the repository root so discovery starts there. If the client starts the server elsewhere, give `--project` an absolute path.

The same shape works in the other clients, with a different file and root key:

| Client | File | Root key |
| --- | --- | --- |
| Claude Code | `.mcp.json` | `mcpServers` |
| VS Code | `.vscode/mcp.json` | `servers`, with `"type": "stdio"` |
| Cursor | `.cursor/mcp.json` | `mcpServers` |

VS Code spells the same server like this:

```json
{
  "servers": {
    "prototest": {
      "type": "stdio",
      "command": "prototest-mcp",
      "args": ["--project", "."],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

Claude Desktop uses the `mcpServers` shape in its own settings file. Its working directory is not your repository, so give `--project` an absolute path there.

## Give the agent the skill

The repository carries one skill that teaches the evidence loop and the four tools: [`skills/prototest-evidence-loop/SKILL.md`](https://github.com/MSeys/ProtoTest/blob/main/skills/prototest-evidence-loop/SKILL.md). It is copy-in, not an install.

A client that reads skills folders (Claude Code, for example) loads it from its skills directory. From a checkout of this repository:

```bash
mkdir -p .claude/skills
cp -r skills/prototest-evidence-loop .claude/skills/
```

Otherwise download the file from the repository and place it in the same layout. A client without a skills folder reads the file as an instruction instead: paste its body into the client's rules or instructions file.

The skill is optional. The MCP server's tool descriptions are the contract, so an agent without the bundle can still list the tools and work from them.

## Where it reads

The server takes one of two inputs, in this order:

- `--trace <file.prototrace>` reads exactly that archive. Use it for a single trace downloaded from CI. While it is set, a `folder` argument to `list_runs` is refused.
- `--project <folder>` discovers the archives under the folder.

Without either, the `PROTOTEST_PROJECT` environment variable is the folder, and without that, the current directory.

Discovery looks at the folder's `TestResults/` first. While that folder yields a readable trace, the rest of the tree is not walked. Otherwise it walks the tree and prunes `bin`, `obj`, `.git` and `node_modules`, so a folder holding only an unreadable archive does not hide readable runs elsewhere. "Newest" is the run's recorded start time, never a file timestamp. An archive the reader cannot open is skipped with a reason in `list_runs` and never guessed at.

The default trace lands below the test project's build output, and discovery prunes `bin`, so the server cannot see it from the repository root. Give the suite a results folder the server can see. The [continuous integration page](../continuous-integration/index.md#put-every-artifact-in-one-place) uses one environment variable, so CI and local runs write to the same place:

```csharp
var results = Environment.GetEnvironmentVariable("PROTOTEST_RESULTS")
    ?? Path.Combine("TestResults", "ProtoTest");

builder.ConfigureTracing(trace =>
    trace.OutputPath = Path.Combine(results, "run.prototrace"));
```

Set `PROTOTEST_RESULTS` to an absolute path such as `<repository>/TestResults` when you run locally. For one archive, `--trace` always works, wherever it was written.

## What the agent can see

Every tool is read-only and returns a compact JSON document.

| Tool | Input | Returns |
| --- | --- | --- |
| `list_runs` | optional `folder`, `limit` (default 10) | the newest runs first: run id, trace file, start and completion, outcome counts, failing test ids, and the archives it had to skip |
| `get_failure` | optional `runId`, `testId` | the failure entry: outcome, error, source location, the selected failing operation, the shape mismatches and the test's artifacts |
| `get_diagnosis` | optional `runId`, `testId`, `detail` (`summary` or `context`) | the run's diagnosis, or one failing test's context package |
| `get_coverage` | optional `runId`, `target`, `category`, `includeUncovered`, `offset`, `limit` | coverage totals and uncovered units from the report the run embedded |

[Diagnosis](./diagnosis.md) explains what `get_failure` and `get_diagnosis` return and what the agent can do with it.

## Check it

Run your suite once so a `.prototrace` exists, then ask the agent to list the runs. It should answer with a run id and a trace file from your machine.

You can see the same story without an agent:

```bash
prototest summary TestResults/run.prototrace
```

`prototest summary` is the `ProtoTest.Cli` tool; install it with `dotnet tool install --global ProtoTest.Cli`. It prints the same diagnosis the MCP tools return, so it is the quickest way to check that the file the agent would read says what you expect. The [CLI reference](./cli.md) documents all four verbs, their arguments and their exit codes.

## Limits

- Read-only: no trace is written, no suite is rerun, the stdio host binds no port, and nothing on disk is modified.
- Nothing leaves the machine by default: no telemetry, no uploads, no accounts. The stdio host reads local archives, stdout carries the protocol only, and logs go to stderr.
- Hard caps bound every payload. `list_runs` returns at most 50 runs, `get_coverage` at most 200 uncovered units, and `get_diagnosis` applies the [diagnosis caps](./diagnosis.md#limits).
- The server reads evidence that already exists. A run with no `.prototrace` is not visible to it; write the trace first.
- One external dependency: the official `ModelContextProtocol` SDK (Apache-2.0).
- The demo-only endpoint is built and not hosted. See [Using ProtoTest with coding agents](./coding-agents.md#the-demo-endpoint-honestly).
