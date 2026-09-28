# MCP test fixtures

Two committed `.prototrace` archives written by the framework itself and read by the
`ProtoTest.Mcp` tools in `RunToolsTests`:

- `run-passed.prototrace` - one green test, no report artifact.
- `run-failed.prototrace` - one green test and one failed test: the `assert.json.shape`
  operation carries real `shape.mismatches` (`$.orderId`, expected 7, actual 42) and the test
  declares a response artifact plus the captured expected shape; the run embeds the JSON report
  a `JsonReportSink` wrote, with two covered and two uncovered units.

They were produced by a throwaway console app that composes `ProtoHostBuilder`, records the
shape assertion through `ProtoShapeAssertion`, and adds the report source and `JsonReportSink`;
nothing is hand-written into the archives. Their recorded source locations name that generator app
(`artifacts/fixture-gen/Program.cs`), so the archives stay readable when the repository's sample
suites change. `NoisyTrace` writes a third, uncommitted trace at test time when a test needs an
oversized run (the token-budget test).
