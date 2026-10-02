# Starter: notes for coding agents

`Starter.Api` is the application. `Starter.Tests` is its ProtoTest suite: integration tests that call the
running API and record every call as evidence in a `.prototrace` file.

## Run

```bash
dotnet tool restore
dotnet test
```

`dotnet tool restore` installs the two ProtoTest tools this project pins: the `prototest` CLI and the
`prototest-mcp` server that `.mcp.json` registers. Run it once after cloning.

## Read evidence, not output

A failing test has a trace. Read it before changing code:

- with MCP: `list_runs`, then `get_failure`, then `get_diagnosis` with `detail=context`;
- without MCP: `dotnet prototest summary <path-to.prototrace>`.

Fix from what the trace shows: the request, the response, the mismatching field, the source line. A fix
is done when `check_fix` (or `dotnet prototest prove <before> <after>`) says proven.

The MCP server offers three prompts that run a whole job: `fix_failure`, `cover_change` and
`improve_tests`.

## Write a test

Before writing one, call `get_suite_map`: it lists the clients, data provisioners, attributes, page
objects and example tests the suite already has. Reuse them. `get_coverage` names the test to extend
or copy for each uncovered endpoint, and `review_tests` must read the new test clean. The skills in `.claude/skills` describe
both workflows step by step (`prototest-evidence-loop`, `prototest-write-test`).

## Conventions

- One behaviour per test, named as a PascalCase sentence (`CreatingAnOrderReturnsIt`).
- Call the API through `Proto.Context.Rest()`, never a new `HttpClient`.
- Assert with `Should.HaveHttpStatus(...)` and `Should.MatchShape(new { ... })`, naming only the fields the
  behaviour depends on.
- No `Task.Delay` or `Thread.Sleep`: move time with `Proto.Context.Clock`, wait with `ProtoPolling.PollAsync`.
- Treat a `PT` warning from the build as a review comment: `ProtoTest.Analyzers` flags a fixed wait
  (`PT0003`) and a hand-made `HttpClient` (`PT0004`) in a test.
- Setup lives in `Starter.Tests/Setup.cs`, not in a test.
- `using` directives go inside the file-scoped namespace.
