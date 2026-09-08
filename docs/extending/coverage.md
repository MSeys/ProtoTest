# Extend coverage

Coverage in ProtoTest is event-based. An integration records a `CoverageHit`; collectors that target the same named client receive it and turn it into report items. The examples assume the relevant integration package and suite setup are already present.

## Use an existing collector

Integrations expose collectors through the shared target builder extension:

```csharp
builder.AddRest(rest => rest
	.AddClient("Orders")
	.WithCoverage<RestCoverageCollector>());
```

For contract coverage:

```csharp
builder.AddRest(rest => rest
	.AddClient("Orders")
	.WithCoverage<OpenApiCoverageCollector>());
```

This keeps integration registration composable. A target can have more than one collector when the suite needs multiple views of the same request traffic.

## Record a hit

A test or integration can record a hit directly:

```csharp
Proto.Context.RecordHit(
	targetName: "Orders",
	identifier: "GET /orders/{id}",
	data: new RestHitData(
		Method: "GET",
		RouteTemplate: "/orders/{id}",
		StatusCode: 200,
		ResponseBody: "{\"id\":42}",
		Headers: new Dictionary<string, string>()));
```

The context stores the hit and dispatches it to matching collectors:

```csharp
var hits = Proto.Context.RecordedHits;
```

Most tests should not need to call `RecordHit` directly; integration request builders normally do this automatically.

## Build a collector

Implement `IProtoCollector` directly for a collector that does not need the standard aggregation behavior:

```csharp
using System.Collections.Concurrent;
using ProtoTest.Core;
using ProtoTest.Rest;

public sealed class ScenarioCollector(string targetName) : IProtoCollector
{
	private readonly ConcurrentBag<CoverageItem> _items = [];

	public string TargetName { get; } = targetName;
	public string Category => "Scenario";

	public void RecordHit(CoverageHit hit)
	{
		_items.Add(new CoverageItem(
			TargetName,
			Category,
			hit.Identifier,
			IsVisited: true,
			HitCount: 1));
	}

	public IEnumerable<CoverageItem> GetReportItems() => _items.ToArray();
}
```

Register it through the target builder:

```csharp
builder.AddRest(rest => rest
	.AddClient("Orders")
	.WithCoverage<ScenarioCollector>());
```

Collectors should support concurrent calls because tests or requests can execute in parallel. Use a lock or concurrent collections for mutable aggregation. Dependencies used by a collector should be registered in the host's service collection and injected through its constructor.

## Build on `ProtoCollector`

`ProtoCollector` provides the common target name and hit storage. Derive from it when the default behavior fits and override `RecordHit` or `GetReportItems` for domain-specific aggregation.

## Use custom state in coverage

If coverage needs scenario metadata, keep that metadata in an `IProtoContext` object and include the relevant information in the `CoverageHit.Data` payload. This lets the test, hook, integration, and collector share a typed scenario without using static state. See [context and state](../guides/context-and-state.md).

## Collector versus sink

A collector turns hits into coverage items. A sink is responsible for sending or persisting results. Use a collector for aggregation and a sink for output or reporting integration.
