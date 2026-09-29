---
sidebar_position: 2
title: Coverage and observations
description: "Coverage of your API's surface rather than its lines: which endpoints, responses, fields and methods your suite actually asserted."
---

import CoverageMap from '@site/src/components/CoverageMap';
import VisibilityPanel from '@site/src/components/VisibilityPanel';

# Coverage and observations

Code coverage tells you which lines ran. It cannot tell you which **parts of your API** your tests actually checked. An endpoint can be called by a setup helper a thousand times and never have its response asserted once.

ProtoTest measures coverage against the *contract*: your OpenAPI document, your GraphQL schema. For REST, it counts a response property as covered only when a shape assertion actually matched it, and gRPC coverage counts the services and methods your calls reached.

## What it is

Coverage is built from **observations**: facts the integrations record while tests run. The integrations record them for you, and you can record your own.

```mermaid
flowchart LR
    Test["Test / integration"] -->|RecordObservation| Context["ProtoExecutionContext"]
    Context --> Collectors["Collectors<br/><small>singletons, whole run</small>"]
    Collectors -->|GetReportItems| Export["Export at end of run"]
    Export --> Sinks["Sinks<br/><small>JSON · HTML · yours</small>"]
    Sinks --> Archive[".prototrace"]
```

1. Integrations record **observations** as tests run. REST records `http.response` for every response and `http.contract.shape` for every successful shape assertion. GraphQL records `graphql.response` and `graphql.contract.shape`; gRPC records `grpc.response` per call and `grpc.contract.shape`; messaging records `messaging.published` and `messaging.receive`, plus `messaging.contract.shape` from a message shape assertion.
2. Each observation is offered to every registered **collector** whose `CanCollect` accepts it. Collectors live for the whole run, so they aggregate across all tests.
3. When the run stops, every collector's **report items** are gathered, sorted by target, category and identifier, and passed to every **sink**.
4. Files the sinks wrote are added to the `.prototrace` archive.

### What the run could see

Where the observations came from matters as much as the counts. The run screen states where the application ran, which capabilities were composed, and which value sources were present or absent. Absence keeps its place, drawn dashed.

<VisibilityPanel />

### Turn it on

```csharp
builder
    .AddApplication("Api", app => app
        .AddRest(rest => rest
            .AddClient("Api")
            .AddCollector<RestCoverageCollector>()
            .AddCollector<OpenApiCoverageCollector>())
        .AddGraphQL(graphQL => graphQL
            .AddClient("GraphQL")
            .WithSchemaCoverage("schema.graphql"))
        .AddGrpc(grpc => grpc
            .AddClient("Projects")
            .AddCollector<GrpcCoverageCollector>()))
    .AddSink<HtmlReportSink>();
```

| Collector | Reports |
| --- | --- |
| `RestCoverageCollector` | every REST endpoint your suite called, with hit counts |
| `RestTrafficCoverageCollector` | the fields that arrived in REST responses but that no shape assertion mentioned, in their own section (opt-in; never counted as covered) |
| [`OpenApiCoverageCollector`](../integrations/openapi.md) | the **whole** OpenAPI document: endpoints, responses and response properties, covered or not |
| [GraphQL schema coverage](../integrations/graphql/coverage.md) | the whole schema: types, fields and arguments, and input types with their input fields |
| `GrpcCoverageCollector` | every gRPC service and method your suite called, from the client's `grpc.response` observations. A failed call records `grpc.failure` and does not count as covered |

Collectors gather; [sinks](./reporting.md) write the results. Without a sink you see nothing.

`AddCollector` hangs off the `IProtoTargetBuilder` returned by a client registration. REST, GraphQL and gRPC all support it. The collector's first constructor argument is the target name, and any extra arguments to `AddCollector` follow it.

### Record your own observations

```csharp
public sealed record ProtoObservation(
    string TargetName,      // the registered target, e.g. "Api", or "Northstar:Api" under an application
    string Kind,            // what kind of fact, e.g. "http.response"
    string Identifier,      // what it is about, e.g. "GET /api/orders/{id}"
    object? Data = null,
    IReadOnlyDictionary<string, object>? Metadata = null);
```

Domain facts that deserve to be in a report are one call away:

```csharp
Proto.Context.RecordObservation(
    targetName: "Billing",
    kind: "invoice.state",
    identifier: invoice.State,
    data: new { invoice.Id, invoice.Total });
```

## How to read it

### Reading the report

The report nests endpoints, responses and properties. A covered item, a partially covered one and a gap read differently, and a property no assertion matched says so:

<CoverageMap />

Three different gaps, three different fixes:

- **An endpoint was never called**: a feature with no test at all.
- **A response status was never reached**: the error path is untested. `403` and `404` are the usual suspects.
- **A property was never asserted**: the test calls the endpoint but does not check that field. Add it to a `Should.MatchShape`.

The summary at the top of each report gives the total, covered and uncovered counts and a coverage percentage.

:::tip[Coverage rewards shape assertions]
Property coverage comes from the paths `Should.MatchShape` matched. A test that only checks the status code covers the endpoint and the status, but none of the fields. That is deliberate: a field nobody asserts is a field that can break silently.
:::

### Traffic coverage (observed but unasserted)

`RestCoverageCollector` counts the routes your suite called; traffic coverage looks inside the responses. It reports the fields that arrived in a response and that no shape assertion mentioned, in its own report section. It is opt-in:

```csharp
builder.AddRest(rest => rest
    .AddClient("Api")
    .AddCollector<RestTrafficCoverageCollector>());
```

```
Traffic (observed but unasserted)
REST traffic  GET /api/v1/organizations/{id} · 200
              └ $.seatCount
              └ $.owner.email
```

Observed fields never count as covered. That is the point. The report's coverage percentage, the run gates and the OpenAPI property table keep counting only what an assertion matched, so a field can appear here and still be uncovered there. The section is built from the observations the run already records: response bodies (`http.response`) and the matched paths of shape assertions (`http.contract.shape`).

The rules, so the section is read correctly:

- The comparison is per method, route template and status code. A field any shape mentioned for that same combination counts as asserted for the run.
- A response no shape touched reports all of its fields. That is the gap the endpoint-level report cannot show.
- `JsonValue.Any()` and `JsonValue.NotNull()` mention the whole value but not the fields inside it, so those fields appear here.
- The collector reads the sanitized bodies the trace already carries. A body truncated by the diagnostic cap cannot be analyzed and contributes nothing.
- A shape assertion made without an execution context records no structured route and cannot claim a field.

## The artifact

Coverage reaches the outside world as **report items**. The [sinks](./reporting.md) receive them once, at the end of the run, and write them into the JSON and HTML reports, which in turn travel inside the `.prototrace` archive. A coverage percentage in CI is the same item tree a run gate reads.

```csharp
public sealed record ProtoReportItem(
    string TargetName,
    string Category,
    string Identifier,
    string Kind = ProtoReportItemKinds.Observation,               // observation, coverage, finding, gate, metric, resource, run_metadata, traffic, or your own
    ProtoReportStatus Status = ProtoReportStatus.Neutral,         // Neutral, Info, Success, Warning, Error
    int Count = 0,
    bool? IsCovered = null,
    double? Value = null,
    string? Unit = null,
    string? Message = null,
    IReadOnlyList<string>? Tags = null,
    IReadOnlyList<ProtoReportItem>? Children = null,
    IReadOnlyDictionary<string, object>? Metadata = null,
    string? DisplayName = null,
    string? DisplayGroup = null);
```

Items nest through `Children`. The kinds cover more than coverage: a `Metric` with a `Value` and `Unit`, or a `Finding` with a `Warning` status and a `Message`, show up in the same reports. A kind is an open string, so an integration can define its own. The built-in ones are named by `ProtoReportItemKinds`, and the HTML report keeps the kinds in their own sections (coverage, traffic, findings, run gates, resources, run metadata). An unknown kind gets its own section titled after it, so a passed gate is never read as a finding.

### Writing a collector

The simplest collector counts identifiers. Derive from `ProtoCoverageCollector` and give it a category:

```csharp
public sealed class InvoiceStateCoverage(string targetName) : ProtoCoverageCollector(targetName)
{
    public override string Category => "Invoice states";

    public override bool CanCollect(ProtoObservation observation) =>
        base.CanCollect(observation) && observation.Kind == "invoice.state";
}
```

The base class matches observations whose `TargetName` equals its own (ignoring case) and records one covered item per distinct `Identifier` with a hit count. `RestCoverageCollector` and `GrpcCoverageCollector` are built the same way. Under an `[Application]` the client is registered under its qualified name (`Api` becomes `Northstar:Api`), and observations carry that same qualified name, so collectors attached to the client keep matching.

To report things that were **not** observed, which is the valuable part, override `GetReportItems` and enumerate the full set, as the OpenAPI collector does with the specification:

```csharp
public sealed class InvoiceStateCoverage(string targetName) : ProtoCoverageCollector(targetName)
{
    private static readonly string[] AllStates = ["draft", "open", "paid", "overdue", "void"];

    // Category and CanCollect as above ...

    public override IEnumerable<ProtoReportItem> GetReportItems()
    {
        lock (_lock)
        {
            return AllStates.Select(state => _items.TryGetValue(state, out var hit)
                ? hit
                : new ProtoReportItem(TargetName, Category, state,
                    Kind: ProtoReportItemKinds.Coverage,
                    Status: ProtoReportStatus.Neutral,
                    IsCovered: false)).ToList();
        }
    }
}
```

### Collectors not tied to a client

For a collector that is not about any client, register it directly:

```csharp
builder.ConfigureServices(services =>
    services.AddSingleton<IProtoCollector>(new InvoiceStateCoverage("Billing")));
```

A messaging collector is one of these: the observation target is the broker name, not a client target, so construct the collector with that name (`RabbitMQ`) and register it directly.

Collectors must be thread-safe, because tests run in parallel. Use the base class's `protected readonly ProtoLock _lock`.

The interfaces underneath are small:

```csharp
public interface IProtoCollector
{
    bool CanCollect(ProtoObservation observation);
    void Collect(ProtoObservation observation);
}

public interface IProtoReportSource
{
    IEnumerable<ProtoReportItem> GetReportItems();
}
```

## Limits

- A field counts as covered only when a shape assertion matched it. A test that checks the status code covers the endpoint and the status, and none of the fields.
- Observed fields never count as covered. The traffic section reports a gap; it does not close it.
- `ProtoTest.Messaging` ships no collector. Its destinations are not a coverage category: the `messaging.published`, `messaging.receive` and `messaging.contract.shape` observations reach a report only through a collector you register with the broker's target name (`RabbitMQ`, or `InMemory` for the default broker). The same is true of `ProtoTest.Messaging.RabbitMq`.
- A body truncated by the diagnostic cap cannot be analyzed and contributes nothing to traffic coverage.
- A shape assertion made without an execution context records no structured route and cannot claim a field.
- A report is a snapshot taken before the run's own resources are released. A run-scoped resource still reads as registered and neutral there, and its release is recorded in the [ProtoTrace](./prototrace.md) afterwards.
- A kind is an open string. The built-in kinds get their own HTML sections and an unknown kind gets one titled after it, so choose a name that reads as a section title.

## Learn more

- [Reporting](./reporting.md): the sinks that write these items out.
- [ProtoTrace](./prototrace.md): the run record the observations travel in.
- [OpenAPI contract coverage](../integrations/openapi.md) and [GraphQL schema coverage](../integrations/graphql/coverage.md): what "covered" means for each contract.
