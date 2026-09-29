---
sidebar_position: 1
title: Using ProtoTest with coding agents
description: The workflow a coding agent follows over ProtoTest evidence, the tools it calls, and what stays on your machine.
---

# Using ProtoTest with coding agents

A failing integration test leaves a lot behind: the test, the operation that failed, the request and the response, the state change, the line of code. ProtoTest reads that evidence and hands it to a coding agent as a small, deterministic document. The agent spends its context on the fix, not on finding out what happened.

This page is the map for the section:

- [Setup](./setup.md) installs the MCP server and points it at your runs.
- [Diagnosis](./diagnosis.md) shows what the agent reads when a test fails.
- [Verification](./verification.md) turns two runs' reports into a pull request verdict.
- [Loop](./loop.md) wires the whole workflow into CI.
- [CLI reference](./cli.md) runs the same evidence from a terminal, with no agent.

The tools here are for your agent. How the project itself uses AI is on the [AI usage](../project/ai-usage.md) page.

## The workflow an agent follows

Say a pull request has one failing test. The agent works through the same steps every time:

| The agent's turn | It calls | It learns |
| --- | --- | --- |
| Find the run | `list_runs` | which run failed and which tests it holds |
| Read the failure | `get_failure` | the error, the source location, the selected failing operation, the mismatches |
| Read the context | `get_diagnosis` with `detail=context` | the ancestors, the nearest call, the section previews, the source snippet, the artifacts, the state changes, the report rows |
| Fix | an editor | the file and line the context named |
| Verify | `prototest verify` | whether the fix regressed a covered unit or changed the specification |
| Report | `prototest feedback` or the action | the digest the pull request reads |

The steps map to the **evidence loop**: fail, evidence, fix, verify, report. The [Loop](./loop.md) page names the same steps with the CI wiring.

## What the agent reads, and what it should not

- The MCP tool descriptions are the contract. An agent that lists tools sees four names, what each reads and that each is read-only.
- Summaries first. Every tool caps its payload, so the agent asks for one failure, one test or one page of coverage. It never pulls a whole trace into context.
- The trace itself is for depth. A `.prototrace` archive holds `spans.json`, `state.json`, embedded sources and artifacts ([ProtoTrace](../observability/prototrace.md)); the MCP tools read it, the viewer shows it.
- One digest, two renderers. `prototest summary` and the pull request comment render the same diagnosis document, so the log the agent read and the comment your reviewer reads cannot disagree.

## The skills bundle

The repository carries one skill an agent can load: [`skills/prototest-evidence-loop/SKILL.md`](https://github.com/MSeys/ProtoTest/blob/main/skills/prototest-evidence-loop/SKILL.md). It teaches the loop on this page, the four MCP tools, when to reach for `prototest verify` and `prototest feedback`, and where the docs are.

It is copy-in, not a package. No NuGet package carries it and no installer writes it: copy the folder into your client's skills directory, or paste the file into the instructions file your client reads. [Setup](./setup.md#give-the-agent-the-skill) shows both.

The skill is optional. The MCP tool descriptions and the CLI output are the contract; the skill only points at them, and this page is written so an agent pointed at it can follow the same commands.

## What is local and what is not

| Surface | Runs where | What leaves the machine |
| --- | --- | --- |
| `prototest-mcp` (stdio) | your machine | nothing |
| `prototest` CLI | your machine | only what you point it at: a pull request comment or a webhook |
| Trace viewer | your browser | nothing; the archive is read in the browser |
| Demo MCP endpoint | the repository, loopback only | nothing; it serves one bundled trace |

Nothing is uploaded by default. There is no telemetry, no account and no ProtoTest service to sign in to.

### The demo endpoint, honestly

`samples/ProtoTest.Mcp.DemoEndpoint` is **built and not hosted**. There is no public address. It serves the same four read-only tools over Streamable HTTP against one bundled demo trace: no filesystem input, no accounts, no uploads, no retention, a fixed window of 60 requests per minute, bound to loopback. The local stdio server stays the product surface. The endpoint exists so a host can be added later without changing the tools.

Run it yourself:

```bash
dotnet run --project samples/ProtoTest.Mcp.DemoEndpoint
```

The endpoint is then at `http://127.0.0.1:5199/`. Point an HTTP-capable MCP client at it and call `list_runs`: one bundled trace, four tools, no account. There is no other surface to try, because the endpoint runs on your machine and nowhere else.

## Check it

Run your suite once so a `.prototrace` exists, then ask your agent:

> List the ProtoTest runs in this repository and read the newest failure.

The agent should answer with a run id, a trace file from your machine and the failing test. If it sees no runs, the server is pointed at a folder that does not hold the archive; [Setup](./setup.md#where-it-reads) explains where discovery looks.
