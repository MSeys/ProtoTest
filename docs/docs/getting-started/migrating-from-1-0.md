---
sidebar_position: 6
title: Migrating from 1.0 to 1.1
description: "What a 1.0 suite changes for 1.1: the renamed package and options section, and the surface that keeps working but is deprecated."
---

# Migrating from 1.0 to 1.1

Moving a 1.0 suite to 1.1 takes three steps: fix what no longer compiles, replace what now warns, and check what changed behavior. Most suites need only the first step.

| Action | What | Count |
| --- | --- | --- |
| MUST | [removed members](#removed-in-11) that no longer compile | 10 |
| SHOULD | [deprecated shims](#deprecated-in-11) that keep compiling with a warning | 4 groups |
| NOTHING | [renamed surface](#renamed-or-replaced) with a fallback, and [behavior changes](#also-changed) to verify | 3 renames, 5 behavior changes |

The changelog's Breaking section lists the calls that must change. The rest of a 1.0 suite runs as it is. This page covers the removed members, the renames and the deprecated surface. The [changelog](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md) is the complete record, including the changes this page does not repeat.

## Removed in 1.1

These members existed in 1.0.1 and no longer exist. Your suite does not compile until you replace them. The changelog's Breaking section has the same list.

| Removed member | Replacement | Docs |
| --- | --- | --- |
| `ProtoExecutionContext.RegisterClient<T>(client, name, bool)` | the third argument is now `ProtoClientOwnership` (`Context` or `Caller`), so a `true` becomes `ProtoClientOwnership.Context` | [Clients](../foundation/clients.md) |
| `ProtoDocumentSource.LoadText(source, baseUrl, httpClient)` | one `LoadText` method with optional parameters replaces the overload | [Extending](../advanced/extending.md) |
| `ProtoHttpAuthLifecycleHook` constructor | sealed, and takes a `ProtoProtocol` instead of a string | [Hooks](../foundation/hooks.md) |
| `ProtoHttpClientResolution` members | a positional record with `ClientEntityName`. `SourceName`, `SourceClientName` and `Deconstruct` are gone, and `ProtoHttpClientResolver.Resolve` takes the client name as an optional third argument | [Extending](../advanced/extending.md) |
| the backend operation's `Result` on `WebOperationContext` | gone | |
| Playwright options `Context` | configure the browser context in code with `ConfigureContext` | [Web](../integrations/web/index.md) |
| `RequiresPlaywrightBrowser.Session` | the condition probes the configured browser or channel | [Skip conditions](../foundation/skip-conditions.md) |
| `GrpcAttachmentOptions` HTTP members | no longer derives from the HTTP attachment options, so `SensitiveHeaders` and `SensitiveQueryParameters` are gone. gRPC metadata redaction uses the gRPC client's `SensitiveMetadataKeys` | [gRPC](../integrations/grpc/index.md) |
| Raw `ServerStreaming` and `DuplexStreaming` helpers | moved from the gRPC client to the blocking facade: `client.Blocking.ServerStreaming(...)` | [gRPC](../integrations/grpc/index.md) |
| the `PollInterval` on `RabbitMqOptions` | awaits are event-driven and `MessagingOptions.DefaultTimeout` bounds them | [Messaging](../integrations/messaging/index.md) |

## Deprecated in 1.1

Each item below still compiles, still records its data, and stays available for all of 1.x. It produces a warning. Use the replacement in new tests.

### Assertions

| 1.0 spelling | Replacement |
| --- | --- |
| `response.ShouldHaveNoErrors()`, `ShouldHaveErrors()`, `ShouldHaveError(code)` | `response.Should.HaveNoErrors()`, `Should.HaveErrors()`, `Should.HaveError(code)` |
| `ProtoGrpcAssertions.ShouldHaveStatus(...)`, `ShouldNotHaveStatus(...)` | `ProtoGrpcAssertions.For(exception).Should.HaveStatus(...)`, `.ShouldNot.HaveStatus(...)` |
| `model.Verify()` | `model.Should.MatchModel()` |
| `column.ShouldAll(predicate)` | `column.Should.All(predicate)` |

The facade and the polarity rules are on [Assertions](../foundation/assertions.md).

### Shape

The old `ShouldMatchShape` methods are obsolete on REST and GraphQL responses, consumed messages and gRPC replies. Use the `Should` form instead. It chains after another assertion:

```csharp
response.Should.HaveHttpStatus(HttpStatusCode.OK).Should.MatchShape(shape);
```

`Should.MatchShape(shape, exact: true)` is the strict form on every shape surface. A field in the response that the shape does not mention counts as a mismatch, and the failure names its path.

A Sheets model row keeps `row.ShouldMatchShape(shape)`. A model row is your own type, and C# cannot give it a `Should` property. A table row uses `row.Should.MatchShape(shape)`. See [Shape matching](../foundation/shape-matching.md).

### Skip-key infrastructure

`AddInfrastructure(piece, keys)` is obsolete. It keeps its rule for all of 1.x: the host skips a piece when every key it declares is configured. `AddInfrastructureAlways` still lets you opt out of that skip. The replacement is a provider chain:

```csharp
builder.AddInfrastructure(
    "MessagingBroker",
    chain => chain
        .UseConfigured()
        .UseContainer(RabbitMqBroker.Container()),
    RabbitMqOptions.ConnectionStringSetting);
```

`UseConfigured()` is the provider that uses the configured environment. `Use(provider)` accepts any `IProtoTargetProvider`. See [Infrastructure](../foundation/infrastructure.md).

### The gRPC options constant

`GrpcAttachmentOptions.ConfigurationSection` is an obsolete alias. Use the inherited `ConfigurationSectionName` property, or the `SectionName` constant where a name is needed without an instance.

## Renamed or replaced

### The MassTransit package

`ProtoTest.MassTransit` is now `ProtoTest.Messaging.MassTransit`, to match the other messaging packages (`ProtoTest.Messaging`, `ProtoTest.Messaging.RabbitMq`). The old package was never released, so nothing you installed changes. If you use it from a branch build, update the `PackageReference`. The public type names (`MassTransitEnvelope`, `UseMassTransit`) are the same.

### The gRPC options section

gRPC client options now bind from `ProtoTest:Grpc:Client`. The 1.0 section `ProtoTest:Grpc` still binds as a deprecated fallback, so an existing `appsettings.json` keeps working. A value under the new section wins. For options that hold a list, the entries from both sections are added together. See [Options and keys](../integrations/grpc/index.md#options-and-keys).

### The OpenTelemetry bridge

The `ProtoTest.OpenTelemetry` package is retired. The `ProtoTest` `ActivitySource` always exists, so you subscribe with one line in your own OpenTelemetry setup:

```csharp
.AddSource("ProtoTest")
```

No package replaces the bridge, because the source was always there. See [OpenTelemetry](../observability/opentelemetry.md).

## Also changed

These 1.1 changes are not deprecations. They can affect a suite that asserts or filters on the old behavior.

- **Shape failures throw the protocol's own exception.** Each exception names its subject:
  - REST throws `RestAssertionException`.
  - GraphQL throws `GraphQLAssertionException`.
  - gRPC throws `GrpcAssertionException`.
  - Messaging throws `MessagingAssertionException`.
  - A Sheets row throws `SpreadsheetAssertionException`.

  `JsonShapeMismatchException`, with the list of mismatches, is still available as the `InnerException`. Code that caught it directly must catch the protocol exception or `ProtoAssertionException` instead.
- **REST object request bodies serialize camelCase by default**, matching GraphQL variables. Pass explicit `JsonSerializerOptions` to keep another naming policy.
- **gRPC client options are per named client.** Each `AddClient` callback applies to that client only, and the shared `ProtoTest:Grpc:Client` section binds over every client.
- **A per-run WireMock fake keeps its stubs and request log for the whole run.** Call `Reset()` when a test must start from a clean fake.
- **The publish observation is `messaging.published`.** The operation is still `messaging.publish`. A collector that filtered on the old observation kind must be updated.

## Next

- [Installation](./installation.md): the package lines for 1.1.
- [Configuration](./configuration.md): the section rules behind the gRPC rename.
- [CHANGELOG](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md): the complete 1.1 record.
