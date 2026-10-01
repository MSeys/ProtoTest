# ProtoTest.Traces

Reads `.prototrace` archives without a dependency on `ProtoTest.Core`.

```bash
dotnet add package ProtoTest.Traces
```

```csharp
var archive = ProtoTraceArchive.Open("TestResults/Shop.prototrace");
foreach (var test in archive.Tests.Where(test => !test.Succeeded))
{
    Console.WriteLine($"{test.Outcome}: {test.Name} ({test.Failure?.Kind})");
}
```

One failure selector serves the CLI, the tools and the viewer: `ProtoTraceTest.Failure` picks the
deepest failing operation, lets an `assert.*` check outrank anything with an error and ranks phase
spans (`test.*`) last. A failed operation outranks a cancelled one whatever their depth, so a
cancelled child that recorded an error never hides the failure above it. `Ancestors`, `CallAncestor`
and `ProtoTraceOperation.ReadMismatches` expose what `ProtoTest.Diagnosis` and the tools build on.

The reader also exposes each operation's sections, moments and recorded evidence (observations,
attachments, findings), the run's attributes and run-level events, the declared artifacts and the
embedded sources with their content on request, the tracked state (`ReadState`) and the embedded JSON
report (`ProtoTraceReport.TryRead`).

`ProtoTraceDiscovery.Discover(folder)` finds the readable runs a folder holds: its `TestResults/`
first, then, when that yields no readable run, the tree with `obj`, `.git` and `node_modules` pruned
and only the `TestResults` folders read inside `bin`, newest first by the run's recorded start time. An archive that cannot be read comes back with
the reason it was skipped instead of failing the scan.

## Limits

- The reader supports span format 2.x archives; older formats are rejected with a clear message.
- It reads `manifest.json` and the spans document named by the manifest; the state document is loaded
  only when `ReadState` asks for it.
- It declares the run- and test-level artifacts an archive carries, and reads a declared artifact's or
  embedded source's content on request from the file it was opened from. An archive read from a stream
  has no content or state to read and says so.
- The embedded report is a projection of the JSON a `ProtoTest.Reporting` sink wrote, capped at 64 MB;
  the coverage arithmetic stays the sink's.

## Learn more

- [ProtoTrace and the trace format](https://prototest.dev/docs/observability/prototrace)
- [CLI reference](https://prototest.dev/docs/agent-workflows/cli)
- [Coding agents and MCP](https://prototest.dev/docs/agent-workflows/coding-agents)
