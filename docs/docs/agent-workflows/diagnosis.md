---
sidebar_position: 3
title: Diagnosis
description: The deterministic document that says which test failed, why, and what the failing operation carried, for a terminal or a coding agent.
---

# Diagnosis

One test in the run fails. The log shows expected `99`, actual `0`. The diagnosis names the request that produced it, the surrounding code, and what the operation changed. It answers all of that from the trace, with recorded evidence and no guessing.

## The summary in a terminal

```bash
dotnet tool install --global ProtoTest.Cli
prototest summary TestResults/run.prototrace
```

Every line of a failing block says one thing:

import AnnotatedCode from '@site/src/components/AnnotatedCode';

<AnnotatedCode
  filename="prototest summary"
  language="text"
  code={`ProtoTest trace 2.0 · run 29e344f9cf54431ca7d8bad3f87a1749 · 2026-09-28 09:55:33Z - 2026-09-28 09:55:33Z
2 tests · 1 failed · 1 succeeded

FAILED orders match their shape (16 ms)
  Shape mismatch failed with 1 error(s):
    • [\$.orderId]: Values did not match. (Expected: '7', Actual: '42')
  at artifacts/fixture-gen/Program.cs:65 (Program.<<Main)
  assert.json.shape · failed
  cause: assertion (1 mismatch)
  mismatch: \$.orderId: expected 7, actual 42`}
  callouts={[
    {line: 1, title: 'The document', note: 'Trace format, run id, and the recorded time range of the run.'},
    {line: 2, title: 'The counts', note: 'Every test by outcome. A partial test passed its runner outcome but something inside it failed, and the summary does not hide it.'},
    {line: 4, title: 'The test', note: 'Outcome, name and duration. Every test that did not fully succeed gets a block like this one.'},
    {line: 5, title: 'The recorded error', note: 'The message the run recorded, so it points at the code that failed.'},
    {line: 7, title: 'The source location', note: 'The file and line of the selected failure.'},
    {line: 8, title: 'The selected operation', note: 'The kind and name of the failing operation, and its status. The kind prints once when it is the name.'},
    {line: 9, title: 'The rule', note: 'The diagnosis rule that matched. An assertion lists its mismatches, and an operation error names the error.'},
    {line: 10, title: 'The mismatches', note: 'Path, expected and actual, capped at three per block with a truncation line.'},
  ]}
  foot={<>A failed run gate gets its own block at the end, with the gate's message and details. The output above is the committed MCP test fixture. A run with nothing to report prints the header, the counts and <code>All green.</code></>}
/>

The [CLI reference](./cli.md) lists the verb's arguments, the exit codes and the other three commands.

## The same document for an agent

`get_diagnosis` returns the same run as one JSON document. The fields an agent works from:

| Field | What it holds |
| --- | --- |
| `digestVersion`, `traceFormatVersion`, `runId`, `traceFile` | which document this is and which archive it came from |
| `startedAtUtc`, `completedAtUtc`, `environment` | when the run happened and on which runtime and OS |
| `outcomes` | the per-outcome test counts |
| `failures` | every test that did not fully succeed: its outcome, the selected failure, the rule that explains it, the mismatches, the findings and the artifacts |
| `gates`, `findings` | the run's own verdicts and the evidence tests recorded |
| `coverage` or `coverageAbsentReason` | the totals the embedded report published, or why there is no report |

`get_failure` returns one test's failure entry when the agent wants the short version. A trimmed result from the same fixture:

```json
{
  "runId": "29e344f9cf54431ca7d8bad3f87a1749",
  "test": {
    "testId": "00002",
    "name": "orders match their shape",
    "outcome": "failed",
    "durationMs": 16.4395
  },
  "failure": {
    "kind": "assert.json.shape",
    "name": "assert.json.shape",
    "phase": "execution",
    "status": "failed",
    "errorType": "ProtoTest.Json.JsonShapeMismatchException",
    "errorMessage": "Shape mismatch failed with 1 error(s):\r\n  • [$.orderId]: Values did not match. (Expected: '7', Actual: '42')",
    "sourceFile": "artifacts/fixture-gen/Program.cs",
    "sourceLine": 65
  },
  "artifacts": [
    { "name": "00002-rest-01-expected-shape", "mediaType": "application/json", "sizeBytes": 29 },
    { "name": "00002-rest-01-response.json", "mediaType": "application/json", "sizeBytes": 30 }
  ]
}
```

The failure selector matches the viewer's. It picks the deepest failing operation. An `assert.*` check outranks an error. Phase operations rank last. A cancelled operation never outranks a failed one. `prototest summary`, the MCP tools and the viewer therefore select the same failure and tell one story.

## The rules

A non-succeeded test is explained by the first rule that matches, in this order:

| Rule | The recorded evidence | The cause the diagnosis states |
| --- | --- | --- |
| assertion | a failed `assert.*` operation with recorded checks or `shape.mismatches` | the mismatch list, with path, expected and actual, and the subject the operation is about |
| operation error | a failed operation with an error type and message | the error plus its ancestor call |
| runner failure | the runner reported a failure and no operation error exists | the message and the recorded source location |
| finding | the runner outcome stayed green but a teardown or rollback finding attached | the finding, and the operation it names |

A failed run gate is not a per-test rule. It gets its own block in the document, as above.

Anything else is reported as **unexplained**, with the run and test ids and a pointer to the viewer. The diagnosis never guesses. If the evidence does not carry a cause, the document says so.

## The context package

`get_diagnosis` with `detail=context` returns what an agent needs to fix one failure. Everything the agent reads arrives around the one operation it has to fix:

```text
                    ancestors (≤ 32)
                          │
   section previews (16) ──┤
   source snippet (41 lines) ─┤
   state changes (≤ 10) ─────┤──▶ the selected failure ──▶ artifacts (≤ 10, metadata)
   the nearest call ancestor ──┘      operation        └─▶ report rows (≤ 10)
```

- the selected failure operation: kind, name, phase, status, error, source file, line, function and the recorded attributes.
- its ancestor chain to the test execution, and the nearest call ancestor (`http.request`, `graphql.operation`, `grpc.call`, a `messaging.*` operation).
- the operation's sections (checks, diff, code, fields) with payload previews.
- the embedded source snippet around the failing line, when the archive carries it. A file over the 512 KB embed cap, or a run with `EmbedSources` off, is absent with that reason.
- the artifacts reachable from the failing operation, listed with media type and size. The MCP tool lists metadata only. Reading the content is a library call (`ReadContext(..., includeArtifactContent: true)`), bounded at 64 KB.
- the state changes the failing operation caused.
- the report rows for the test: findings, run gates and the coverage row the operation touched.

## Review what a test proves

A green test can still prove little. `prototest review` and the `review_tests` tool read what each test recorded and name three things:

| Rule | What was recorded | Next step |
| --- | --- | --- |
| `no-check` | the test body ran operations and no check | assert on the answer: `Should.HaveStatus`, `Should.MatchShape`, a message await |
| `unchecked-call` | a call that no check looked at before the next call | check what it returned, or move it into setup if only its side effect matters |
| `untraced-gap` | 250 ms or more of the test body with no operation | replace the sleep with a wait that records what it waits for, or call through a ProtoTest client |

A check is an `assert.*` operation or an await for a message, so it fails the test when the answer is wrong. Only the test body (the execution phase) is reviewed; setup may call without checking. The gap uses the viewer's rule for an untraced gap, and a review reports it only from 250 ms, so scheduling noise never becomes a finding.

```text
ProtoTest review: run ed41245e...
1 tests · 0 clean · 1 untraced-gap

FailureDrills.ARealWaitDoesNotCloseTheDueWindow (failed, 2 checks, 1 calls)
  untraced-gap: 1.0 s of the test body recorded no operation, starting 160 ms in, before 'REST · GET /api/v1/organization'.
    at samples/Northstar.ProtoTest/FailureDrills.cs:38
    next: Replace a sleep with a wait that records what it waits for (ProtoPolling, a message await, the test clock), or call through a ProtoTest client so the call is traced.
```

A review cannot see a path a test never ran. A missing failure path is a coverage question: read `get_coverage`.

## Limits

- Deterministic and offline: one archive and one report in, one JSON document out. No model runs inside ProtoTest, no network call is made, and nothing is written. The same archive produces byte-identical JSON.
- Coverage, gates and findings come from the JSON report a `ProtoTest.Reporting` sink embedded. Without a report, coverage is omitted with the reason, and gates and findings fall back to the trace's own records. Coverage is never recomputed from the trace.
- Hard caps bound every document and context:

| What is capped | The cap |
| --- | --- |
| mismatches in one failure | 25 |
| findings, run gates and artifacts in one document | 10 each |
| an error message | 4,000 characters |
| one section preview | 4 KB |
| the source snippet | 41 lines, 512 KB of embedded source |
| the ancestor chain | 32 |
| state items in the context package | 10 |
| sections in the context package | 16 |
| coverage rows in the context package | 10 |

- An archive read from a stream has no file to read, so its state, sources and artifact content are absent with that reason. Open the file instead.
- The rules read recorded evidence only. A failure whose evidence was not recorded is unexplained, and the viewer is the place to read the rest of the story.

Run `prototest summary` on your own trace, or ask your agent for `get_diagnosis` with `detail=context` on the newest failure. The output names the file, the line and the subject, which is enough to open the code and fix it.
