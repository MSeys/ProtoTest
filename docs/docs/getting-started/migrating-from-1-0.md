---
sidebar_position: 6
title: Migrating from 1.0 to 1.1
description: "What a 1.0 suite changes for 1.1: the renamed package and options section, and the surface that keeps working but is deprecated."
---

# Migrating from 1.0 to 1.1

| Action | What | Count |
| --- | --- | --- |
| MUST | [removed members](#removed-in-11) that no longer compile | 10 |
| SHOULD | [deprecated shims](#deprecated-in-11) that keep compiling with a warning | 4 groups |
| NOTHING | [renamed surface](#renamed-or-replaced) with a fallback, and [behavior changes](#also-changed) to verify | 3 renames, 5 behavior changes |

The changelog's Breaking section lists the calls that must change; the rest of a 1.0 suite runs as it is. This page covers the renames and the deprecated surface. The [changelog](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md) is the complete record, including the changes this page does not repeat.

## Removed in 1.1

These members existed in 1.0.1 and are gone. The changelog's Breaking section carries the same list.

| Removed member | Replacement | Docs |
| --- | --- | --- |
| `ProtoExecutionContext.RegisterClient<T>(client, name, bool)` | the third argument is now `ProtoClientOwnership` (`Context` or `Caller`), so a `true` becomes `ProtoClientOwnership.Context` | [Clients](../foundation/clients.md) |
| `ProtoDocumentSource.LoadText(source, baseUrl, httpClient)` | one `LoadText` method with optional parameters replaces the overload | [Extending](../advanced/extending.md) |
| `ProtoHttpAuthLifecycleHook` constructor | sealed, and takes a `ProtoProtocol` instead of a string | [Hooks](../foundation/hooks.md) |
| `ProtoHttpClientResolution` members | a positional record with `ClientEntityName`; `SourceName`, `SourceClientName` and `Deconstruct` are gone, and `ProtoHttpClientResolver.Resolve` takes the client name as an optional third argument | [Extending](../advanced/extending.md) |
| the backend operation's `Result` on `WebOperationContext` | gone | |
| Playwright options `Context` | configure the browser context in code with `ConfigureContext` | [Web](../integrations/web/index.md) |
| `RequiresPlaywrightBrowser.Session` | the condition probes the configured browser or channel | [Skip conditions](../foundation/skip-conditions.md) |
| `GrpcAttachmentOptions` HTTP members | no longer derives from the HTTP attachment options, so `SensitiveHeaders` and `SensitiveQueryParameters` are gone; gRPC metadata redaction uses the gRPC client's `SensitiveMetadataKeys` | [gRPC](../integrations/grpc/index.md) |
| Raw `ServerStreaming` and `DuplexStreaming` helpers | moved from the gRPC client to the blocking facade: `client.Blocking.ServerStreaming(...)` | [gRPC](../integrations/grpc/index.md) |
| the `PollInterval` on `RabbitMqOptions` | awaits are event-driven and `MessagingOptions.DefaultTimeout` bounds them | [Messaging](../integrations/messaging/index.md) |

## Deprecated in 1.1

Each item below keeps compiling and keeps its records for 1.x. New tests use the replacement.

### Assertions

| 1.0 spelling | Replacement |
| --- | --- |
| `response.ShouldHaveNoErrors()`, `ShouldHaveErrors()`, `ShouldHaveError(code)` | `response.Should.HaveNoErrors()`, `Should.HaveErrors()`, `Should.HaveError(code)` |
| `ProtoGrpcAssertions.ShouldHaveStatus(...)`, `ShouldNotHaveStatus(...)` | `ProtoGrpcAssertions.For(exception).Should.HaveStatus(...)`, `.ShouldNot.HaveStatus(...)` |
| `model.Verify()` | `model.Should.MatchModel()` |
| `column.ShouldAll(predicate)` | `column.Should.All(predicate)` |

The facade and the polarity rules are on [Assertions](../foundation/assertions.md).

### Shape

The old `ShouldMatchShape` spellings are obsolete shims on REST and GraphQL responses, consumed messages and gRPC replies. Use the facade form, which chains after another assertion:

```csharp
response.Should.HaveHttpStatus(HttpStatusCode.OK).Should.MatchShape(shape);
```

`Should.MatchShape(shape, exact: true)` is the exhaustive form on every shape surface: a field present in the response that the shape does not mention is a mismatch naming its path. A Sheets model row keeps `row.ShouldMatchShape(shape)`, because a record is a user type and C# cannot give it a `Should` property; a table row uses `row.Should.MatchShape(shape)`. See [Shape matching](../foundation/shape-matching.md).

### Skip-key infrastructure

`AddInfrastructure(piece, keys)` is obsolete. It keeps its rule for 1.x: a piece whose every declared key is configured is skipped, and `AddInfrastructureAlways` keeps the opt-out. The replacement is the provider chain:

```csharp
builder.AddInfrastructure(
    "MessagingBroker",
    chain => chain
        .UseConfigured()
        .UseContainer(RabbitMqBroker.Container()),
    RabbitMqOptions.ConnectionStringSetting);
```

`UseConfigured()` is the configured-environment provider, and `Use(provider)` takes any `IProtoTargetProvider`. See [Infrastructure](../foundation/infrastructure.md).

### The gRPC options constant

`GrpcAttachmentOptions.ConfigurationSection` is an obsolete alias. Use the inherited `ConfigurationSectionName` property, or the `SectionName` constant where a name is needed without an instance.

## Renamed or replaced

### The MassTransit package

One package changes its name: `ProtoTest.MassTransit` is `ProtoTest.Messaging.MassTransit`, matching the messaging family's nesting (`ProtoTest.Messaging`, `ProtoTest.Messaging.RabbitMq`). It was never released, so nothing you installed changes. Update the `PackageReference`; the public type names (`MassTransitEnvelope`, `UseMassTransit`) are unchanged.

### The gRPC options section

gRPC client options bind `ProtoTest:Grpc:Client`. The 1.0 section `ProtoTest:Grpc` still binds as a deprecated fallback, so an existing `appsettings.json` keeps working. A value under the current section wins, and list-valued options accumulate the fallback and current entries. See [Options and keys](../integrations/grpc/index.md#options-and-keys).

### The OpenTelemetry bridge

The `ProtoTest.OpenTelemetry` package retired. The `ProtoTest` `ActivitySource` always exists, so subscribing is one line in your own OpenTelemetry setup:

```csharp
.AddSource("ProtoTest")
```

No package replaces the bridge, because the source was always there. See [OpenTelemetry](../observability/opentelemetry.md).

## Also changed

A few 1.1 changes are not deprecations, and they can affect a suite that asserts or filters on the old behavior.

- **Shape failures throw the protocol exception.** A REST mismatch throws `RestAssertionException`, GraphQL `GraphQLAssertionException`, gRPC `GrpcAssertionException`, messaging `MessagingAssertionException` and a Sheets row `SpreadsheetAssertionException`, each naming its subject. `JsonShapeMismatchException`, with the mismatch list, stays reachable as the `InnerException`. Code that caught it directly catches the protocol exception or `ProtoAssertionException`.
- **REST object request bodies serialize camelCase by default**, matching GraphQL variables. Pass explicit `JsonSerializerOptions` to keep another naming policy.
- **gRPC client options are per named client.** Each `AddClient` callback applies to that client only, and the shared `ProtoTest:Grpc:Client` section binds over every client.
- **A per-run WireMock fake keeps its stubs and request log for the whole run.** Call `Reset()` when a test must start from a clean fake.
- **The publish observation is `messaging.published`.** The operation stays `messaging.publish`; a collector that filtered the old observation kind updates.

## Next

- [Installation](./installation.md): the package lines for 1.1.
- [Configuration](./configuration.md): the section rules behind the gRPC rename.
- [CHANGELOG](https://github.com/MSeys/ProtoTest/blob/main/CHANGELOG.md): the complete 1.1 record.
