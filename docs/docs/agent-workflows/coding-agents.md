---
sidebar_position: 1
title: Coding agents
sidebar_label: Coding agents
description: The workflow a coding agent follows over ProtoTest evidence, the tools it calls, and what stays on your machine.
---

# Coding agents

A failing test produces the test, the failed operation, the request and response, the state change, and the code line. ProtoTest reads that evidence and hands it to a coding agent as a small, deterministic document. The agent spends its context on the fix, not on finding out what happened.

Your agent reads that document from a local read-only MCP server. Nothing leaves the machine.

Ask for the newest failure:

> **You:** List the ProtoTest runs in this repository and read the newest failure.
>
> **Agent:** Run `761778e6dc82498a9f9965fa1e6b5a24`, 1 test, 1 failed.
> `TheAddressWasHardcodedForOneMachine` failed with a `ConnectionError reaching
> http://127.0.0.1:5099: connection refused` at `Program.cs:65`, in `test.execution`.

The run id above stands in for any run, because ids and times are new on every run.

## The agent does the work, ProtoTest judges it

An agent can say a test is fixed or a change is covered. ProtoTest checks the claim against what the runs recorded. Each job ends with a tool that decides whether it is done:

| The job | The agent | ProtoTest judges with | Done when |
| --- | --- | --- | --- |
| Fix a failing test | reads the failure, compares with the last green run, edits code or test, reruns | `check_fix` | the receipt says proven: the baseline failed, every rerun passed, nothing else broke |
| Cover a change | writes or extends the test the coverage suggestion names | `get_coverage`, `review_tests` | the new units read covered, and the test checks what it calls |
| Improve tests | applies each review finding's next step | `review_tests`, `compare_runs` | the tests read clean and no test broke |

Each job is also an MCP prompt (`fix_failure`, `cover_change`, `improve_tests`), so a client that shows prompts starts the whole job from one. Every job starts from `get_suite_map`, which lists the clients, data provisioners, attributes, page objects and example tests the suite already has. The agent writes in your suite's style instead of inventing new setup.

## Fixing a failure, step by step

| The agent's turn | It calls | It learns |
| --- | --- | --- |
| Read the failure | `list_runs`, `get_failure` | which run failed, the error, the source location, the failing operation, the mismatches |
| Find what changed | `compare_runs` | the operation where this run left the last green one |
| Read the context | `get_diagnosis` with `detail=context` | the ancestors, the nearest call, the section previews, the source snippet, the artifacts, the state changes |
| Fix | an editor | the file and line the evidence named |
| Prove | a rerun, then `check_fix` | proven, or each unmet condition with the run it is about. Not proven sends the agent back to Fix |
| Report | the [evidence action](../continuous-integration/index.md#the-evidence-action) | the pull request comment with what broke and what was fixed |

The steps map to the **evidence loop**: fail, evidence, fix, verify, report. [The evidence loop](./loop.md) shows the pull request comment they produce.

## The pages in this section

- [Setup](./setup.md) installs the MCP server and points it at your runs.
- [Diagnosis](./diagnosis.md) shows what the agent reads when a test fails, and how a review says what each test proves.
- [Verification](./verification.md) turns two runs into a pull request verdict.
- [The evidence loop](./loop.md) wires the whole workflow into CI.
- [CLI reference](./cli.md) runs the same evidence from a terminal, with no agent.

The tools here are for your agent. How the project itself uses AI is on the [AI usage](../project/ai-usage.md) page.

## What the agent reads, and what it should not

- The MCP tool descriptions are the contract. An agent that lists tools sees eight names, what each reads and that each is read-only, and three prompts.
- Summaries first. Every tool caps its payload, so the agent asks for one failure, one test or one page of coverage. It never pulls a whole trace into context.
- The trace itself is for depth. A `.prototrace` archive holds `spans.json`, `state.json`, embedded sources and artifacts ([ProtoTrace](../observability/prototrace.md)). The MCP tools read it, and the viewer shows it.
- `prototest summary` and the pull request comment render the same diagnosis document. The agent log and the reviewer comment match.

## The skills bundle

The repository carries two skills. [`skills/prototest-evidence-loop/SKILL.md`](https://github.com/MSeys/ProtoTest/blob/main/skills/prototest-evidence-loop/SKILL.md) covers the loop, the MCP tools, `prototest verify`, `prototest feedback`, and the docs. [`skills/prototest-write-test/SKILL.md`](https://github.com/MSeys/ProtoTest/blob/main/skills/prototest-write-test/SKILL.md) covers a new test: read the suite with `get_suite_map`, reuse its clients, provisioners, attributes and page objects, then prove the test with two runs and `prototest verify`.

They are copy-in, not a package. A project made with `dotnet new prototest` starts with both in `.claude/skills/`, next to an `AGENTS.md` and a `.mcp.json`. Otherwise copy the folders into your client's skills directory, or paste the file into the instructions file your client reads. [Setup](./setup.md#give-the-agent-the-skill) shows both.

The skills are optional. The MCP tool descriptions and the CLI output are the contract, and the skills only point at them. This page is written so an agent pointed at it can follow the same commands.

## What is local and what is not

| Surface | Runs where | What leaves the machine |
| --- | --- | --- |
| `prototest-mcp` (stdio) | your machine | nothing |
| `prototest` CLI | your machine | only what you point it at: a pull request comment or a webhook |
| Trace viewer | your browser | nothing, because the archive is read in the browser |
| Demo MCP endpoint | your machine, loopback only | nothing, because it serves one bundled trace |

Nothing is uploaded by default. There is no telemetry, no account and no ProtoTest service to sign in to.

### Demo endpoint

`samples/ProtoTest.Mcp.DemoEndpoint` is a sample project. Run it on your own machine and it serves the same eight read-only tools and three prompts over Streamable HTTP against one bundled demo trace. It takes no filesystem input. It enforces 60 requests per minute and binds to loopback.

```bash
dotnet run --project samples/ProtoTest.Mcp.DemoEndpoint
```

The endpoint is then at `http://127.0.0.1:5199/`: the root path, with no `/mcp` prefix. Point a client that speaks MCP Streamable HTTP at it (for example MCP Inspector) and call `list_runs`: one bundled trace, eight tools, no account. A plain JSON-RPC POST without the Streamable HTTP headers is answered `406`. The local stdio server is the surface that reads your repository, and this one reads the bundled trace.

## Check it

Run your suite once so a `.prototrace` exists, then ask the question at the top of this page. If the agent sees no runs, the server is pointed at a folder that does not hold the archive. [Setup](./setup.md#where-it-reads) explains where discovery looks.
