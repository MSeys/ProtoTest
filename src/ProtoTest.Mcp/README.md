# ProtoTest.Mcp

> Preview: the surface can change before 1.2.

Reads `.prototrace` evidence over the Model Context Protocol, so a coding agent can list runs, read a
failure, read the diagnosis and read coverage without leaving the machine or opening the viewer.

```bash
dotnet tool install --global ProtoTest.Mcp
prototest-mcp --project .
```

Register it with an MCP client, for example `.mcp.json`:

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

Where it reads, in precedence order:

- `--trace <file.prototrace>` reads exactly that one archive.
- `--project <folder>` discovers `.prototrace` archives under the folder: its `TestResults/` first,
  then, when that yields no readable run, the tree with `bin`, `obj`, `.git` and `node_modules`
  pruned. While `TestResults/` yields a readable trace, the tree is not walked; a folder holding only
  an unreadable archive does not hide runs elsewhere.
- `PROTOTEST_PROJECT` is the folder fallback; without any input, the current directory.
- "Newest" is the greatest recorded `runStartedAtUtc`, never a file timestamp. An unreadable archive
  is skipped with a named reason in `list_runs` and never guessed at.
- The scan is `ProtoTest.Traces`' `ProtoTraceDiscovery.Discover`, the one shared with the `prototest`
  CLI's `index` command.

## Tools

Every tool is read-only and returns a compact JSON document.

| Tool | Input | Returns |
| --- | --- | --- |
| `list_runs` | optional `folder`, `limit` (default 10) | the newest runs first: run id, trace file, start/completion, outcome counts and the failing test ids, plus the archives it had to skip |
| `get_failure` | optional `runId`, `testId` | the failure entry: outcome, error, source location, the selected failing operation, the recorded shape mismatches and the test's evidence artifacts. Defaults to the newest run's first non-succeeded test |
| `get_diagnosis` | optional `runId`, `testId`, `detail` (`summary`/`context`) | the deterministic diagnosis: the digest with each failure's rule and mismatches, the run gates, findings and coverage; `detail=context` returns the failing test's context package (ancestors, call, section previews, source snippet, artifacts, state, report rows) |
| `get_coverage` | optional `runId`, `target`, `category`, `includeUncovered`, `offset`, `limit` | the coverage totals and the uncovered units from the JSON report a `ProtoTest.Reporting` sink embedded; states a missing report instead of inventing numbers |

## Limits

- Read-only: no trace is written, no suite is rerun, no port is bound by the stdio host, and nothing
  on disk is modified.
- Nothing leaves the machine by default: no telemetry, no uploads, no accounts. The stdio host reads
  local archives and logs to stderr; stdout carries the protocol only.
- Hard caps bound every payload: 50 runs, 20 failing tests per run, 25 mismatches, 20 artifacts per
  test, 200 uncovered units, a 64 MB report read and a 4,000-character error message. `list_runs`
  clamps `limit` to 1-50 and `get_coverage` clamps `limit` to 1-200. `get_diagnosis` applies the
  diagnosis library's own caps (25 mismatches, 10 findings, 10 artifacts, 4 KB previews, 32 ancestors).
- `get_failure` and `get_diagnosis` report the same failure selection the viewer uses: the deepest
  failing operation, an `assert.*` check outranking anything with an error, phase spans last, and a
  cancelled operation never outranking a failed one.
- `get_coverage` and `get_diagnosis` read the JSON report artifact a reporting sink embedded
  (`ProtoTest.Reporting`); without one they say so and do not recompute coverage from spans.
- One external dependency: the official `ModelContextProtocol` SDK (Apache-2.0).
- The demo-only endpoint lives in `samples/ProtoTest.Mcp.DemoEndpoint`: built in this repository and
  **not hosted**; see its README for the full honest-state list. The local stdio server stays the product surface.
