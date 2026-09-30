---
sidebar_position: 2
title: The .prototrace archive
description: "What the .prototrace file holds: the ZIP layout, the spans and state documents, format compatibility, run metadata and lifetime."
---

# The .prototrace archive

The companion to [ProtoTrace](./prototrace.md). The viewer page is how you read a run; this page is what the file is.

## The file format

A `.prototrace` file is a ZIP archive:

```
run.prototrace
├── manifest.json            { "formatVersion": "2.0", "spansEntry": "spans.json", "stateEntry": "state.json" }
├── spans.json               what ran: one resource group per test and one for the run
├── state.json               what existed and changed: tracked items with their changes
├── sources/1/OrderTests.cs   the code an operation's location points at, when embedded
└── resources/
    ├── {testId}/artifact-1/{testId}-rest-01-response
    ├── {testId}/artifact-2/{testId}-playwright-default-trace.zip
    └── run/HtmlReportSink/run-artifact-1/report.html
```

- **`spans.json`** holds a resource group per test (its id, name, class, method, outcome and duration) with its operations, their events, and the artifacts the test declared. The run's own group carries its id, start and end, and the environment it ran in (`environment.runtime`, `environment.os`, and any [run metadata you configured](#correlating-a-trace-with-the-run-that-produced-it)); run-level events such as gate verdicts sit on it too.
- **`state.json`** holds the run's tracked items and each test's: kind, id, name, scope, first and last seen, the state at the end and every change, with the operation that caused it.
- **`sources`** in the manifest maps each recorded `code.file.path` to its embedded copy.
- Each artifact is declared once, with its media type, size and path in the archive. An attachment event refers to it by id. The path keeps the name the attachment was given and prefixes the test's id, so a name only has to be unique per test; a run-level artifact keeps its sink's name (`HtmlReportSink` above).

Entries are stored **uncompressed**, so a browser can read the archive without a decompression library. Property names are camelCase. The archive manifest is format **2.0**, its spans document is 2.0, and its state document is **1.1**: tracked values are generic `value` items with the domain type in the id. The live snapshot exposed to code (`host.Trace.Snapshot()`) reports the same span format version.

Test artifacts, every [attachment](../foundation/attachments.md), live under the test's id. Run-level artifacts, such as the reports written by [sinks](./reporting.md), live under `resources/run/`.

### Format compatibility

| Reader | spans 2.x | state 1.x | state 2.x |
| --- | --- | --- | --- |
| Library, CLI and MCP server | reads | reads | needs a library release first |
| Viewer | reads | reads | reads |

The library reads manifest and spans **2.x** and state documents **1.x**; anything else fails with a message naming the version rather than guessing. A state bump the viewer can already read still needs a library release before the CLI opens it. A reader only opens an archive from its own era.

The machine-readable wire facts live in [`design/prototrace-wire.contract.json`](https://github.com/MSeys/ProtoTest/blob/main/design/prototrace-wire.contract.json): the archive, spans and state versions both test suites assert against. Changing a value there is a format change.

### If the process dies

The archive is written once, at the end of the run, after the gates and the reports. A process killed mid-run writes no `.prototrace`. What survives instead is the runner's own output and anything the run already released. An incremental flush (spans per completed test) is not implemented, so if you need evidence from a process that dies, keep the runner's console output and any artifacts the run had already published.

## Correlating a trace with the run that produced it

Every trace carries the run's own id, its timing and the environment it executed on. When the run happens in CI or a shared environment, name the facts that identify the build:

```csharp
builder.ConfigureTracing(trace =>
{
    trace.RunMetadataEnvironmentVariables.Add("GITHUB_RUN_ID"); // read from the process environment
    trace.RunMetadataEnvironmentVariables.Add("GITHUB_SHA");
    trace.RunMetadata["pipeline"] = "nightly";                 // or set a value in code
});
```

Each entry is written as `environment.{key}` on the run's resource group in `spans.json`, next to the built-in `environment.runtime` and `environment.os`, and every [report sink](./reporting.md) receives it as a run-metadata item. A downloaded `.prototrace`, and the report it carries, names the build and run it came from.

The named variables are read once, when the host is built. A variable that is unset or empty, as on a local run, contributes nothing, and neither the trace nor the report changes. An explicit `RunMetadata` value overrides a variable of the same name. ProtoTest does not detect a CI provider by itself: only the variables you name are read, and a name that would hide one of the built-in facts (`runtime`, `os`, `processArchitecture`, `osArchitecture`) fails the build. Values are recorded exactly as given, so list only variables that are safe to carry in a trace.

The viewer's run model reads every `environment.*` entry, but its run header still shows only the runtime and the operating system. Read the metadata from `spans.json` or from the JSON or HTML report's Run metadata section.

## Limits

| The limit | When it matters |
| --- | --- |
| The archive is written once, at the end | A run killed mid-process leaves no archive; keep the runner's console output |
| Redaction covers ProtoTest's own capture | Your own attributes, attachments and run metadata can still carry application data, so treat a trace like test output |
| Run metadata values are recorded exactly as given | List only variables that are safe to travel in a trace |
| `EmbedSources` and `EmbedArtifacts` control what the archive carries; the [benchmarks page](../project/benchmarks.md) records what a trace costs at 100 and 1,000 tests and the levers that change it | A trace that travels to people who should not read the suite's code should turn `EmbedSources` off |
| The library reads 2.x spans and 1.x state documents; the viewer accepts state 1.x or 2.x | An archive outside those versions needs a reader from its own era |

## Learn more

- [ProtoTrace](./prototrace.md): read the run in the viewer or from the terminal.
- [Coverage and observations](./coverage.md): the intentional facts the reports are built from.
- [Reporting](./reporting.md): the JSON and HTML sinks whose files travel inside the archive.
- [OpenTelemetry](./opentelemetry.md): the same operations, exported to a tracing backend.
