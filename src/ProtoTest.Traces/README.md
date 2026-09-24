# ProtoTest.Traces

Reads `.prototrace` archives without a dependency on `ProtoTest.Core`.

```csharp
var archive = ProtoTraceArchive.Open("TestResults/Shop.prototrace");
foreach (var test in archive.Tests.Where(test => !test.Succeeded))
{
    Console.WriteLine($"{test.Outcome}: {test.Name} — {test.Failure?.ErrorMessage}");
}
```

`ProtoTraceSummaryText.Write(archive, writer)` renders the compact failure digest — the run, the outcome
counts, and for every test that did not fully succeed its error, source location and failing operation.

## Limits

- The reader supports span format 2.x archives; older formats are rejected with a clear message.
- It reads `manifest.json` and the spans document named by the manifest; the state document and attached
  artifacts are not loaded unless a consumer asks for them.
