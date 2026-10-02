---
name: prototest-evidence-loop
description: Use when a ProtoTest integration test fails, when reading ProtoTest run evidence, or when a pull request changes what a ProtoTest suite covers. Teaches the evidence loop over the ProtoTest MCP tools and the prototest CLI.
---

# The ProtoTest evidence loop

ProtoTest records every integration test as evidence: the test, the operation that failed, the request
and response, the state change, the source line. One `.prototrace` archive carries it, with the run's
report embedded. This skill teaches the loop over that evidence.

The loop: **fail, evidence, fix, verify, report**. Work one step at a time and read evidence instead of
guessing.

## The MCP tools

With the ProtoTest MCP server registered (`prototest-mcp`), eight read-only tools return compact JSON:

| Tool | Use it to |
| --- | --- |
| `list_runs` | find the newest run and the tests it holds; a run that did not pass is named |
| `get_failure` | read one failure: outcome, error, source location, the selected failing operation, the mismatches |
| `get_diagnosis` | read the run's diagnosis, or with `detail=context` the package needed to fix one failure |
| `get_coverage` | read coverage totals and uncovered units from the run's report |
| `get_suite_map` | read what the suite composes and reuses before writing a new test; the `prototest-write-test` skill covers that work |
| `compare_runs` | see where a run left an earlier one: each broken, fixed or still-failing test with the operation that changed |
| `check_fix` | prove a fix: the receipt says proven only when the baseline failed, the rerun passes and nothing broke |
| `review_tests` | see what each test proves: a body with no check, an unchecked call, an untraced gap, with the next step |

The server also offers three prompts that run a whole job and end with its judge: `fix_failure` (ends
with `check_fix`), `cover_change` (ends with `get_coverage` and `review_tests`) and `improve_tests` (ends
with `review_tests` and `compare_runs`). Start a job from its prompt when the client shows prompts.

Start with `list_runs`, then `get_failure` for the short entry, then `get_diagnosis` with
`detail=context` once you are fixing. Call `get_coverage` when the change touches what the run covers.
Every tool caps its payload, so ask for one run, one test or one page at a time. Never pull a whole
trace into context.

## Walk it through

A pull request has one failing integration test.

1. **Fail.** `list_runs` names the newest run and the failing test.
2. **Evidence.** `get_failure` gives the error, the source location and the failing operation.
   `get_diagnosis` with `detail=context` adds the ancestors, the nearest call, the section previews,
   the source snippet, the artifacts, the state changes and the report rows.
3. **Fix.** Edit the file and line the context named. The subject (the route, the operation, the row)
   tells you what the failure is about.
4. **Verify.** Rerun the suite, then call `check_fix` (or `prototest prove before.prototrace
   after.prototrace`). The fix is done only when the receipt says proven: the baseline failed, every
   rerun passed, no other test broke and the embedded reports verify. `compare_runs` shows what the
   rerun changed when the receipt is not proven.
5. **Report.** Run `prototest feedback run.prototrace` to post the digest, or let the ProtoTest
   Feedback action do it in CI. The digest is the document `prototest summary` prints as text.

Without an MCP client, the CLI reads the same evidence:

- `prototest summary <trace>` prints the digest of one run.
- `prototest index <folder>` writes a static page over a folder of runs, with one digest per run.
- `prototest verify <baseline> <current>` returns the pull request verdict.
- `prototest compare <baseline> <current>` names where each changed test left the baseline.
- `prototest prove <baseline> <current>...` returns the fix receipt.
- `prototest review <trace>` says what each test proves.
- `prototest feedback <trace>` posts the digest, the annotations or a webhook.

## Rules

- Read-only: the tools and the verbs never write a trace, never rerun a test and never modify the
  working tree.
- Nothing leaves the machine by default. A channel posts only when a target is configured.
- Evidence or it did not happen. A cause is stated only from recorded evidence; an `unexplained`
  failure means the cause was not recorded, so open the archive in the viewer.
- Summaries first. Ask for the one failure the task is about.

## Where the docs are

- Using ProtoTest with coding agents: https://prototest.dev/docs/agent-workflows/coding-agents
- Setup, install and discovery: https://prototest.dev/docs/agent-workflows/setup
- Diagnosis, the rules and the caps: https://prototest.dev/docs/agent-workflows/diagnosis
- Verification, the verdict: https://prototest.dev/docs/agent-workflows/verification
- Loop, the CI wiring: https://prototest.dev/docs/agent-workflows/loop
