# ProtoTest documentation

ProtoTest is short for **prototype testing**. It does not mean testing software prototypes. It means bringing the simplicity, speed, and readability of prototyping back to integration testing.

ProtoTest is built around a shared test lifecycle, a scoped `Proto.Context`, named clients, reusable hooks, and integration-specific assertions. The goal is to make integration tests feel easy to write, easy to read, and easy to change.

## What “prototype testing” means

When prototyping, you can try an idea without first building a large amount of ceremony around it. ProtoTest brings that approach to integration tests: the setup is reusable, the test intent stays visible, and the framework takes care of the surrounding lifecycle.

## Start here

If this is your first ProtoTest project:

1. [Install ProtoTest and write your first test](getting-started/first-test.md)
2. [Write tests with `Proto.Context`](guides/writing-tests.md)
3. [Configure clients and reusable class attributes](guides/clients-and-attributes.md)
4. [Share typed state between tests and hooks](guides/context-and-state.md)

## Choose your goal

| I want to… | Read |
| --- | --- |
| Write my first test | [Getting started](getting-started/first-test.md) |
| Write REST tests | [REST integration](integrations/rest.md) |
| Test an ASP.NET Core application | [ASP.NET Core integration](integrations/aspnetcore.md) |
| Measure REST endpoints against OpenAPI | [OpenAPI integration](integrations/openapi.md) |
| Reuse setup and teardown | [Hooks](extending/hooks.md) |
| Share data across a test, hook, and collector | [Context and state](guides/context-and-state.md) |
| Put common settings on a test class | [Clients and attributes](guides/clients-and-attributes.md) |
| Create a custom client | [Custom attributes and clients](extending/custom-attributes-and-clients.md) |
| Add a coverage collector | [Coverage extensions](extending/coverage.md) |
| Choose an extension point | [Extension guide](extending/index.md) |
| Understand ordering and disposal | [Lifecycle reference](reference/lifecycle.md) |
| Compare ProtoTest with plain tests | [With and without ProtoTest](comparisons/with-and-without-prototest.md) |

## The ProtoTest mental model

A test framework invokes the test. The ProtoTest adapter creates the surrounding host and test context, then integration packages add clients and behavior to that context.

```text
Test framework
	  |
	  v
ProtoTest adapter
	  |
	  v
ProtoHost
	  |
	  +-- IProtoRunHook          once per suite
	  +-- IProtoTestHook         around every test
	  +-- ProtoAttribute         class/method behavior
	  +-- IProtoClientInitializer
	  +-- IProtoCollector
	  |
	  v
Proto.Context (current test)
	  |
	  +-- services
	  +-- named clients
	  +-- typed IProtoContext state
	  +-- coverage hits
```

The normal test flow is:

```text
suite setup
  -> run hooks
  -> create test context
  -> initialize named clients
  -> before-test hooks and attributes
  -> test method
  -> after-test hooks and attributes
  -> dispose clients and context
```

You do not need to implement this lifecycle yourself. Select the adapter for your test framework and mark methods with `[ProtoTest]`.

## What is available?

### Test frameworks

- NUnit
- xUnit
- xUnit v3
- MSTest
- TUnit

### Implemented integrations

- REST clients, authentication, JSON bodies, response matching, and REST coverage
- ASP.NET Core `WebApplicationFactory` clients and application-service access
- OpenAPI endpoint, status-code, and response-property coverage

See the [integration overview](integrations/overview.md) for package names and status.

### Extension points

| Need | Use |
| --- | --- |
| Run once before or after the suite | `IProtoRunHook` |
| Run setup or cleanup around every test | `IProtoTestHook` |
| Declare behavior on a class or method | `ProtoAttribute` |
| Create a named test-scoped resource | `IProtoClientInitializer` |
| Share typed scenario data | `IProtoContext` and `Proto.Context.Context<T>()` |
| Aggregate execution data | `IProtoCollector` or `ProtoCollector` |
| Export coverage items | `IProtoSink` |

Read [hooks](extending/hooks.md) for lifecycle extensions or [custom attributes and clients](extending/custom-attributes-and-clients.md) for new test behavior and resources.

## Runnable examples

- [REST demo](examples/rest-demo.md)
- [ASP.NET Core demo](examples/aspnetcore-demo.md)
- [REST comparison](comparisons/with-and-without-prototest.md)

The REST demo is the best complete example: it uses WireMock.Net as an external-style API, named clients, class-level authentication, fluent authentication, response matching, and coverage.

## Reference

- [Lifecycle](reference/lifecycle.md)
- [Execution context](reference/execution-context.md)
- [Extension points](reference/extension-points.md)
- [Extension guide](extending/index.md)
- [Test framework adapters](integrations/nunit.md)
