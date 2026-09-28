# ProtoTest.Diagnosis

Reads one `.prototrace` run and states the deterministic causal summary: which tests failed and why,
what the failing operation carried and changed, and what the report published.

```csharp
var diagnosis = ProtoDiagnosis.Read("TestResults/Shop.prototrace");
foreach (var failure in diagnosis.Failures)
{
    Console.WriteLine($"{failure.Outcome}: {failure.Name} ({failure.Rule})");
}
```

The rules are the same ones `prototest summary` and the MCP `get_diagnosis` tool use, so a CI
log, an agent and the viewer select one failure and tell one story:

- A failed `assert.*` operation with recorded checks or `shape.mismatches` is an **assertion**; the
  cause is the mismatch list and the subject the operation is about.
- A failed operation with an error (a client, lifecycle or protocol call) is an **operation error**.
- A runner-reported failure without a recorded operation error is a **runner failure**.
- A finding explains a test the runner did not fail (**partial** outcomes).
- A failed run gate explains a red run in the gates list.

Anything else is unexplained, with the run and test ids in the document and a pointer to the viewer.
`ProtoDiagnosis.ReadContext(archive, testId)` returns what an agent reads for one failure: the
selected operation with its attributes, the ancestor chain and the nearest call, section previews,
the embedded source snippet, the test's artifacts, the state changes the operation caused and the
report rows the operation touched.

## Limits

- Deterministic and offline: one report and one archive in, one JSON document out. No LLM, no
  network, no coverage re-derivation from spans, no reconstruction of evidence that was not recorded.
  The same archive produces byte-identical JSON.
- Coverage, gates and findings come from the JSON report a `ProtoTest.Reporting` sink embedded. When
  the report is absent, coverage is omitted with the reason and gates and findings are read from the
  run's own trace records; a gate verdict from the trace carries its details as the one recorded
  string.
- Hard caps bound every document and context: 25 mismatches, 10 findings, 10 gates, 10 artifacts,
  4,000-character messages, 4 KB section previews, 41 source-snippet lines, 32 ancestors, 10 state
  items, 16 sections and 10 coverage rows. Artifact content is only read on request and only up to a
  64 KB preview.
- An archive read from a stream has no file to read, so its state, sources and artifact content are
  absent with that reason; open the file instead.

`ProtoDiagnosisJson.ToJson` is the one JSON rendering the MCP tools and the feedback channels share,
and `ProtoTraceSummaryText.Write` is the plain-text rendering `prototest summary` prints.
