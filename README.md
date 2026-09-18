<img width="1600" height="600" alt="ProtoTest blueprint logo banner" src="assets/brand/prototest-banner.svg" />

> Integration testing made as simple, readable, and frictionless as prototyping.

ProtoTest is short for **prototype testing**: not testing software prototypes, but bringing the simplicity, speed, and readability of prototyping back to integration testing. It is a .NET toolkit with a shared test lifecycle, a scoped `Proto.Context`, named clients, reusable hooks, and integration-specific assertions.

## Why ProtoTest?

Prototype testing is about trying an idea quickly and seeing the result clearly. ProtoTest applies that same feeling to integration tests: keep the test readable and focused on behavior while the framework manages the repeatable infrastructure around it.

ProtoTest provides:

- one lifecycle across NUnit, xUnit, xUnit v3, MSTest, and TUnit;
- per-test services, clients, typed state, and coverage hits;
- REST, GraphQL, ASP.NET Core, and component-based Web integrations;
- shared JSON shape matching through `ProtoTest.Json`;
- shared HTTP integration foundations through `ProtoTest.Http`;
- OpenAPI-driven REST coverage;
- extension points for hooks, attributes, clients, and collectors;
- automatic portable execution traces for lifecycle and integration operations.
- an OpenTelemetry bridge through `ProtoTest.OpenTelemetry`: it registers ProtoTest's `ActivitySource` so an exporter you configure (such as Sentry's official OpenTelemetry bridge) can pick up ProtoTest operations.

## Example

```csharp
[ProtoTest]
[Application("Orders")]
[Auth<BearerTokenAuthenticator>("orders-token")]
public async Task GetOrder_ReturnsExpectedOrder()
{
	var response = await Proto.Context.Rest()
		.GetAsync("/orders/{id}", new { id = 42 });

	response
		.ShouldHaveHttpStatus(HttpStatusCode.OK)
		.ShouldMatchShape(new { id = 42, status = "confirmed" });
}
```

This is an illustrative test excerpt. The complete setup is in the [getting-started guide](docs/docs/getting-started/first-test.md).

## Documentation

Read the [ProtoTest documentation](docs/docs/index.md), or start directly with:

- [First test](docs/docs/getting-started/first-test.md)
- [REST integration](docs/docs/integrations/rest/index.md)
- [GraphQL integration](docs/docs/integrations/graphql/index.md)
- [Web integration](docs/docs/integrations/web/index.md)
- [ASP.NET Core integration](docs/docs/integrations/aspnetcore.md)
- [Hooks and lifecycle extensions](docs/docs/foundation/hooks.md)
- [Context and state](docs/docs/foundation/execution-context.md)
- [Extension points](docs/docs/advanced/extending.md#building-an-integration)
- [Execution tracing](docs/docs/advanced/prototrace.md)
- [Extension guide](docs/docs/advanced/extending.md)

## Demo

[ProtoTest.Demo](samples/ProtoTest.Demo) is the single end-to-end showcase. It runs ten journeys against Northstar, a multi-tenant release/deployment control-plane SaaS: onboarding and plan limits; preview/production delivery and rollback; usage metering, invoicing, payments, proration and cancellation; the role and token-scope matrix, tenant isolation and rate limiting; arranging through the domain instead of the API; the same application over gRPC; awaiting a published `invoice.paid` event through RabbitMQ; asserting the generated monthly spreadsheet; and a real browser journey. The journeys reach Northstar through REST, GraphQL (including a live subscription), signed webhooks, gRPC, messaging, spreadsheets and the browser. It also demonstrates parallel tenant provisioning, OpenAPI/GraphQL coverage, custom hooks, clients, contexts, observations, attachments, reports, and a complete ProtoTrace.

## Build and test

The repository contains VSTest projects as well as Microsoft Testing Platform projects. Run the checked-in entrypoint so every adapter and demo is included:

```powershell
./eng/test.ps1
```

CI uses the same command and validates every packable NuGet project afterwards.

To produce the complete package set locally:

```powershell
./eng/pack.ps1
```

## Status

| Area | Status |
| --- | --- |
| Core lifecycle and context | Available |
| NUnit, xUnit, xUnit v3, MSTest, TUnit | Available |
| REST | Available |
| ASP.NET Core | Available |
| OpenAPI | Available |
| GraphQL | Preview |
| Web with Playwright and Selenium backends | Preview |
| gRPC (`ProtoTest.Grpc`) | Available |
| Messaging (`ProtoTest.Messaging`, `ProtoTest.Messaging.RabbitMq`) | Available |
| Spreadsheets (`ProtoTest.Sheets`) | Available |
| Test data (`ProtoTest.Data`) | Available |
| SQL (`ProtoTest.Sql`, `ProtoTest.Sql.EntityFrameworkCore`, `ProtoTest.Sql.Testcontainers`) | Available |
| Containers (`ProtoTest.Testcontainers`, `ProtoTest.Messaging.RabbitMq.Testcontainers`) | Available |
| Coverage | Available |

See the [integration overview](docs/docs/integrations/overview.md) for package names and capabilities.
