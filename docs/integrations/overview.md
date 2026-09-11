# Integrations

ProtoTest separates the test framework adapter from the system-under-test integration. Choose the adapter used by the test project, then add the integration packages needed by the suite.

If you are new to ProtoTest, start with the [first test guide](../getting-started/first-test.md), then continue through the [user guides](../guides/writing-tests.md).

The user guides explain how to write and extend tests. Integration pages explain package-specific setup. Reference pages describe lifecycle and API behavior for users building integrations or debugging advanced scenarios.

## Available integrations

| Integration | Package | Purpose | Example |
| --- | --- | --- | --- |
| Core | `ProtoTest.Core` | Lifecycle, hooks, context, clients, observations, and reporting contracts | [Core lifecycle](../concepts/core-lifecycle.md) |
| HTTP | `ProtoTest.Http` | Shared HTTP client initialization, response buffering, and limits for protocol integrations | — |
| JSON | `ProtoTest.Json` | Protocol-independent JSON shape and value matching used by REST and GraphQL | — |
| NUnit | `ProtoTest.NUnit` | NUnit test lifecycle integration | [Adapter guide](nunit.md) |
| xUnit | `ProtoTest.Xunit` | xUnit v2 lifecycle integration | [Adapter guide](nunit.md#other-frameworks) |
| xUnit v3 | `ProtoTest.Xunit3` | xUnit v3 lifecycle integration | [Adapter guide](nunit.md#other-frameworks) |
| MSTest | `ProtoTest.MSTest` | MSTest lifecycle integration | [Adapter guide](nunit.md#other-frameworks) |
| TUnit | `ProtoTest.TUnit` | TUnit lifecycle integration | [Adapter guide](nunit.md#other-frameworks) |
| REST | `ProtoTest.Rest` | Named HTTP clients, authentication, matching, and REST coverage | [REST guide](rest.md) |
| GraphQL | `ProtoTest.GraphQL` | Fluent operations, GraphQL assertions, and SDL field coverage | [GraphQL guide](graphql.md) |
| ASP.NET Core | `ProtoTest.AspNetCore` | In-memory application hosting and service access | [ASP.NET Core guide](aspnetcore.md) |
| OpenAPI | `ProtoTest.OpenApi` | Specification-driven REST coverage | [OpenAPI guide](openapi.md) |
| Reporting | `ProtoTest.Reporting` | End-of-run JSON and self-contained HTML reports | [Reporting and coverage](../extending/coverage.md) |

The framework adapter guides are intentionally short because the lifecycle is shared. The REST, ASP.NET Core, and OpenAPI guides contain the domain-specific setup and examples.

## Planned integrations

The following projects exist as planned extension points but are not currently implemented integrations:

| Project | Planned purpose |
| --- | --- |
| `ProtoTest.Grpc` | gRPC client and coverage integration |
| `ProtoTest.Playwright` | Browser automation integration |

## Common flow

Regardless of the integration, an adapter follows the same test lifecycle:

```text
suite setup
  -> run hooks
  -> StartTestAsync
	   -> create scope
	   -> initialize clients
	   -> run before hooks
  -> test body
  -> CompleteTestAsync
	   -> run after hooks
	   -> dispose clients and scope
```

Continue with the [first test guide](../getting-started/first-test.md), then choose the integration page for the system under test.
