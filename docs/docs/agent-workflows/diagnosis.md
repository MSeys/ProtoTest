---
sidebar_position: 3
title: Diagnosis
description: The deterministic document that says which test failed, why, and what the failing operation carried, for a terminal or a coding agent.
---

# Diagnosis

A run has dozens of tests and one of them fails. The log says an assertion expected `99` and got `0`. The interesting part is which request produced that number, what the code around it looked like, and what the operation changed. The diagnosis answers all of that from the trace, with recorded evidence and no guessing.

## The summary in a terminal

```bash
dotnet tool install --global ProtoTest.Cli
prototest summary TestResults/run.prototrace
```

The [CLI reference](./cli.md) lists the verb's arguments, the exit codes and the other three commands.

Here is that output for the committed MCP test fixture, `tests/ProtoTest.Mcp.Tests/Fixtures/run-failed.prototrace`. It is abridged; every test that did not fully succeed gets a block like these.

```text
ProtoTest trace 2.0 · run 29e344f9cf54431ca7d8bad3f87a1749 · 2026-09-28 09:55:33Z - 2026-09-28 09:55:33Z
2 tests · 1 failed · 1 succeeded

FAILED orders match their shape (16 ms)
  Shape mismatch failed with 1 error(s):
    • [$.orderId]: Values did not match. (Expected: '7', Actual: '42')
  at artifacts/fixture-gen/Program.cs:65 (Program.<<Main)
  assert.json.shape · failed
  cause: assertion (1 mismatch)
  mismatch: $.orderId: expected 7, actual 42
```

The summary reads top to bottom:

- The first line names the trace format, the run id and the recorded time range.
- The second counts every test by outcome. A **partial** test passed its runner outcome but something inside it failed, and the summary does not hide it.
- Each block starts with the outcome, the test name and its duration.
- The error message and the `at` line come from the failure the run recorded, so they point at the code that failed.
- The operation line names the selected failing operation and its status.
- The `cause` line is the diagnosis rule. An assertion lists its mismatches; an operation error names the error.
- A failed run gate gets its own block at the end, with the gate's message and details.

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

The failure selector is the one the viewer uses: the deepest failing operation, an `assert.*` check outranking anything with an error, phase spans last. `prototest summary`, the MCP tools and the viewer therefore select the same failure and tell one story.

## The rules

A non-succeeded test is explained by the first rule that matches, in this order:

| Rule | The recorded evidence | The cause the diagnosis states |
| --- | --- | --- |
| assertion | a failed `assert.*` operation with recorded checks or `shape.mismatches` | the mismatch list, with path, expected and actual, and the subject the operation is about |
| operation error | a failed operation with an error type and message | the error plus its ancestor call |
| runner failure | the runner reported a failure and no operation error exists | the message and the recorded source location |
| finding | the runner outcome stayed green but a teardown or rollback finding attached | the finding, and the operation it names |

A failed run gate is not a per-test rule; it gets its own block in the document, as above.

Anything else is reported as **unexplained**, with the run and test ids and a pointer to the viewer. The diagnosis never guesses. If the evidence does not carry a cause, the document says so.

## The context package

`get_diagnosis` with `detail=context` returns what an agent needs to fix one failure:

- the selected failure operation: kind, name, phase, status, error, source file, line, function and the recorded attributes;
- its ancestor chain to the test execution, and the nearest call ancestor (`http.request`, `graphql.operation`, `grpc.call`, a `messaging.*` operation);
- the operation's sections (checks, diff, code, fields) with payload previews;
- the embedded source snippet around the failing line, when the archive carries it. A file over the 512 KB embed cap, or a run with `EmbedSources` off, is absent with that reason;
- the artifacts reachable from the failing operation, listed with media type and size. The MCP tool lists metadata only; reading the content is a library call (`ReadContext(..., includeArtifactContent: true)`), bounded at 64 KB;
- the state changes the failing operation caused;
- the report rows for the test: findings, run gates and the coverage row the operation touched.

## Limits

- Deterministic and offline: one archive and one report in, one JSON document out. No model runs inside ProtoTest, no network call is made, and nothing is written. The same archive produces byte-identical JSON.
- Coverage, gates and findings come from the JSON report a `ProtoTest.Reporting` sink embedded. Without a report, coverage is omitted with the reason, and gates and findings fall back to the trace's own records. Coverage is never recomputed from spans.
- Hard caps bound every document and context: 25 mismatches, 10 findings, 10 gates, 10 artifacts, 4,000-character messages, 4 KB section previews, 41 source-snippet lines, 32 ancestors, 10 state items, 16 sections and 10 coverage rows.
- An archive read from a stream has no file to read, so its state, sources and artifact content are absent with that reason. Open the file instead.
- The rules read recorded evidence only. A failure whose evidence was not recorded is unexplained, and the viewer is the place to read the rest of the story.

Run `prototest summary` on your own trace, or ask your agent for `get_diagnosis` with `detail=context` on the newest failure. The output names the file, the line and the subject, which is enough to open the code and fix it.
