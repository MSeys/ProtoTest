---
sidebar_position: 2
title: Coverage and observations
description: "Coverage of your API's surface rather than its lines: which endpoints, responses, fields and methods your suite actually asserted."
---

# Coverage and observations

Code coverage tells you which lines ran. It can't tell you which **parts of your API** your tests actually checked — an endpoint can be called by a setup helper a thousand times and never have its response asserted once.

ProtoTest measures coverage against the *contract*: your OpenAPI document, your GraphQL schema. For REST, it counts a response property as covered only when a shape assertion actually matched it, and gRPC coverage counts the services and methods your calls reached.

## Turning it on

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
| [`OpenApiCoverageCollector`](../integrations/openapi.md) | the **whole** OpenAPI document: endpoints → responses → response properties, covered or not |
| [GraphQL schema coverage](../integrations/graphql/coverage.md) | the whole schema: types → fields → arguments, and input types → input fields |
| `GrpcCoverageCollector` | every gRPC service/method your suite called, from the client's `grpc.response` observations |

Collectors gather; [sinks](./reporting.md) write the results. Without a sink you won't see anything.

`AddCollector` hangs off the `IProtoTargetBuilder` returned by a client registration — REST, GraphQL and gRPC all support it. The collector's first constructor argument is the target name, and any extra arguments to `AddCollector` follow it.

## Reading the report

```
OpenAPI  GET /api/v1/organization                12 hits  ✓
         └ 200                                   12 hits  ✓
           └ $.seatCount                          12 hits  ✓
           └ $.projectCount                        9 hits  ✓
           └ $.cancelAtPeriodEnd                   0 hits  ○   never asserted
         └ 403                                    0 hits  ○   never reached
OpenAPI  POST /api/v1/deployments/{id}/rollback   0 hits  ○   never called
```

Three different gaps, three different fixes:

- **An endpoint was never called** — there's a feature with no test at all.
- **A response status was never reached** — the error path is untested. `403` and `404` are the usual suspects.
- **A property was never asserted** — the test calls the endpoint but doesn't check that field. Add it to a `ShouldMatchShape`.

The summary at the top of each report gives the total, covered and uncovered counts and a coverage percentage.

:::tip[Coverage rewards shape assertions]
Property coverage comes from the paths `ShouldMatchShape` matched. A test that only checks the status code covers the endpoint and the status, but none of the fields. That's deliberate — a field nobody asserts is a field that can break silently.
:::

## How it works

```mermaid
flowchart LR
    Test["Test / integration"] -->|RecordObservation| Context["ProtoExecutionContext"]
    Context --> Collectors["Collectors<br/><small>singletons, whole run</small>"]
    Collectors -->|GetReportItems| Export["Export at end of run"]
    Export --> Sinks["Sinks<br/><small>JSON · HTML · yours</small>"]
    Sinks --> Archive[".prototrace"]
```

1. Integrations record **observations** as tests run. REST records `http.response` for every response and `http.contract.shape` for every successful shape assertion; GraphQL records `graphql.response` and `graphql.contract.shape`; gRPC records `grpc.response` per call and `grpc.contract.shape`; messaging records `messaging.publish` and `messaging.receive`, plus `messaging.contract.shape` from a message shape assertion.
2. Each observation is offered to every registered **collector** whose `CanCollect` accepts it. Collectors live for the whole run, so they aggregate across all tests.
3. When the run stops, every collector's **report items** are gathered, sorted by target, category and identifier, and passed to every **sink**.
4. Files the sinks wrote are added to the `.prototrace` archive.

:::warning[Messaging ships no collector]
`ProtoTest.Messaging` records `messaging.publish`, `messaging.receive` and `messaging.contract.shape` observations, but the package contains no collector, so they never appear in a report unassisted. Register a collector of your own with the broker's target name (`RabbitMQ`, or `InMemory` for the default broker) if you want destinations aggregated. The same is true of `ProtoTest.Messaging.RabbitMq`.
:::

## Observations

```csharp
public sealed record ProtoObservation(
    string TargetName,      // the registered target, e.g. "Api", or "Northstar:Api" under an application
    string Kind,            // what kind of fact, e.g. "http.response"
    string Identifier,      // what it's about, e.g. "GET /api/orders/{id}"
    object? Data = null,
    IReadOnlyDictionary<string, object>? Metadata = null);
```

You can record your own — domain facts that deserve to be in a report:

```csharp
Proto.Context.RecordObservation(
    targetName: "Billing",
    kind: "invoice.state",
    identifier: invoice.State,
    data: new { invoice.Id, invoice.Total });
```

## Writing a collector

The simplest collector counts identifiers. Derive from `ProtoCoverageCollector` and give it a category:

```csharp
public sealed class InvoiceStateCoverage(string targetName) : ProtoCoverageCollector(targetName)
{
    public override string Category => "Invoice states";

    public override bool CanCollect(ProtoObservation observation) =>
        base.CanCollect(observation) && observation.Kind == "invoice.state";
}
```

The base class matches observations whose `TargetName` equals its own (ignoring case), and records one covered item per distinct `Identifier` with a hit count. That's exactly how `RestCoverageCollector` and `GrpcCoverageCollector` are built. Under an `[Application]` the client is registered under its qualified name (`Api` becomes `Northstar:Api`), and observations carry that same qualified name, so collectors attached to the client keep matching.

To report things that were **not** observed — the valuable part — override `GetReportItems` and enumerate the full set, as the OpenAPI collector does with the specification:

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

For a collector that isn't about any client, register it directly:

```csharp
builder.ConfigureServices(services =>
    services.AddSingleton<IProtoCollector>(new InvoiceStateCoverage("Billing")));
```

A messaging collector is one of these: the observation target is the broker name, not a client target, so construct the collector with that name (`RabbitMQ`) and register it directly.

Collectors must be thread-safe; tests run in parallel. Use the base class's `protected readonly ProtoLock _lock`.

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

## Report items

```csharp
public sealed record ProtoReportItem(
    string TargetName,
    string Category,
    string Identifier,
    string Kind = ProtoReportItemKinds.Observation,               // observation, coverage, finding, gate, metric, resource, or your own
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

Items nest through `Children`, and the kinds cover more than coverage: a `Metric` with a `Value` and `Unit`, or a `Finding` with a `Warning` status and a `Message`, show up in the same reports. A kind is an open string, so an integration can define its own; the built-in ones are named by `ProtoReportItemKinds`. The HTML report keeps the kinds in their own sections — coverage, findings, run gates, resources — and gives an unknown kind its own section titled after it, so a passed gate is never read as a finding.

## Links

- [Reporting](./reporting.md) writes these items out; [ProtoTrace](./prototrace.md) records the observations alongside the run.
- [OpenAPI contract coverage](../integrations/openapi.md) and [GraphQL schema coverage](../integrations/graphql/coverage.md) define what "covered" means for each contract.
