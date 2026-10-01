---
sidebar_position: 5
title: gRPC
description: "A named gRPC client per test for unary and streaming calls, with metadata authentication, shape and status assertions, per-call tracing and method coverage."
---

# gRPC

`ProtoTest.Grpc` gives each test a named gRPC client for unary and streaming calls, with metadata authentication, shape and status assertions, per-call tracing and method coverage.

```csharp
[Application("Api", "Grpc:Api")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task GetOrder()
    {
        var reply = await Proto.Context.Grpc().UnaryAsync(
            OrdersMethods.GetOrder,
            new GetOrderRequest { Id = 42 });

        ProtoGrpcAssertions.For(reply).Should.MatchShape(new { id = 42, status = "PENDING" });
    }
}
```

Run it with `dotnet test`. A green run prints `Passed GetOrder`, and the trace lands at `TestResults/prototest-{runId}.prototrace` with a `grpc.call` operation for the call.

## What it adds

Authentication goes through the same `[Auth]` pipeline as REST and GraphQL, and each call is traced and counted for service/method coverage. The client is created during setup and recorded as a client entity. Its channel is created lazily on first use, and both are released with the test.

## Install

```bash
dotnet add package ProtoTest.Grpc
```

The package depends on `ProtoTest.Http` and `ProtoTest.Json`. See [Installation](../../getting-started/installation.md) for the supported .NET versions.

## Compose

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddGrpc(grpc => grpc.AddClient("Api")));
```

The channel address comes from the first of these that exists:

1. the explicit argument to `AddClient`
2. `ProtoTest:Applications:{app}:Grpc:Address`
3. the application's `BaseUrl`
4. the application's in-process transport, when the application is hosted with `AddAspNetCoreServer`.

Calling `AddGrpc` twice does not throw. The shared setup runs once, and each callback still adds its clients.

## The tasks

The `GetOrder` test above is the whole pattern: resolve the client, call, assert the shape. [Calls and assertions](./calls.md) holds the streaming helpers, the descriptor holder and the status assertions.

### Going further

#### Authentication

gRPC uses the same `[Auth]` attributes as the HTTP protocols, including the
[built-in test user](../rest/authentication.md#built-in-test-user), whose header arrives as `prototest-user`
metadata:

```csharp
[Application("Api", "Grpc:Api")]
[Auth<BearerTokenAuthenticator>("token")]
public sealed class OrderTests
{
    // ...
}
```

Authenticators write HTTP headers as usual, and the gRPC applier translates them to lowercase metadata keys. Metadata is layered client `Metadata` first, then `ConfigureMetadata`, then per-call `metadata`. Authenticators run last so they can override.

Each call resolves its authenticator from the test's factory, so a stateful authenticator is created per call and never shared between calls.

Sensitive metadata values are redacted in the trace. `SensitiveMetadataKeys` starts with the defaults listed under [Options and keys](#options-and-keys), and entries under `ProtoTest:Grpc:Client:SensitiveMetadataKeys` extend them. Matching is case-insensitive and by substring, so `authorization` also covers `proxy-authorization`.

#### Attachments

```csharp
builder.AddGrpc(grpc => grpc
    .CaptureAttachments(options => options.MaxDiagnosticBodyLength = 16 * 1024)
    .AddClient("Api"));
```

With capture enabled, each traced call attaches its request and response messages as JSON, named `grpc-{client}-{service}-{method}-{request|response}-{n}`. `{client}` is the sanitized client name, and `n` is the call's position in the client's call sequence. Repeated calls to the same method stay distinct, even from two clients in one test.

Values run through the shared redaction rules, which cover JSON properties such as `password` and `token` and the sensitive metadata keys. Each attachment is capped at `MaxDiagnosticBodyLength` from `ProtoTest:Grpc:Attachments`.

`ClientStreamingAsync` and `ServerStreamingAsync` capture up to the first 10 streamed messages and record the total count in the attachment description. A message that cannot be serialized is reported as a `grpc.attachment.failed` event and never fails the call.

#### Coverage

The async call helpers record a `grpc.response` observation per successful call. Register the collector on the target returned by `AddClient` to aggregate service/method coverage:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api")
    .AddCollector<GrpcCoverageCollector>())
```

`GrpcCoverageCollector` has category `gRPC` and counts one hit per `grpc.response` observation, identified by `{service}/{method}` (see [coverage](../../observability/coverage.md)).

#### Address resolution

A resolver runs per test with the test context. An address that only exists at call time still works, such as a container started by the suite or a per-test tenant host:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api", context => context.Configuration.GetValue<Uri>("Api:Grpc")))
```

The resolver overloads register the client with a deferred address, so initialization succeeds even before the address is known. A resolver returning null at call time is an error. A non-absolute address passed to `AddClient` throws `ArgumentException`.

### Reference: resolution and options

```csharp
IProtoHostBuilder AddGrpc(this IProtoHostBuilder builder, Action<ProtoGrpcBuilder>? configure = null);
IProtoApplicationBuilder AddGrpc(this IProtoApplicationBuilder application, Action<ProtoGrpcBuilder>? configure = null);

// On ProtoGrpcBuilder:
IProtoTargetBuilder AddClient(string name = "Default", string? address = null,
    Action<GrpcClientOptions>? configure = null);
IProtoTargetBuilder AddClient(string name,
    Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> addressResolver,
    Action<GrpcClientOptions>? configure = null);
IProtoTargetBuilder AddClient(string name,
    Func<ProtoExecutionContext, Uri> addressResolver, Action<GrpcClientOptions>? configure = null);
ProtoGrpcBuilder CaptureAttachments(Action<GrpcAttachmentOptions>? configure = null);
```

```csharp
public static ProtoGrpcClient Grpc(this ProtoExecutionContext context, string? clientName = null);
```

Inside `[Application]` the test uses the bound client unless you pass a name. Resolution tries `{application}:{name}`, then the name itself. A host-registered client stays reachable from inside an application, and so does a name exactly one other application registered. A name used by two applications needs the qualified form (`App:Client`). A miss lists the registered gRPC client names.

When no initialized client matches, the application's in-process transport backs a fallback client, and a `grpc.client.resolve` event is written. The fallback is registered so every call shares one channel. Otherwise the accessor throws naming the client, `AddAspNetCoreServer`, and `ProtoTest:Applications:{application}:Grpc:Address`. A call that reaches the channel with no address to resolve throws `InvalidOperationException`. A call after the client was disposed throws `ObjectDisposedException`.

```
explicit AddClient address? → ProtoTest:Applications:{app}:Grpc:Address? → app BaseUrl?
  → in-process transport? → else fallback client + grpc.client.resolve event
resolver overload → deferred to first call (null at call time is an error)
```

The call helpers, which ones are traced, their signatures, the method-descriptor holder and both assertion facades are on [Calls and assertions](./calls.md).

#### Options and keys

| Key | Option | Type | Default |
| --- | --- | --- | --- |
| `ProtoTest:Grpc:Client:Metadata` | `GrpcClientOptions.Metadata` | `IDictionary<string, string>` | empty |
| `ProtoTest:Grpc:Client:DefaultDeadline` | `GrpcClientOptions.DefaultDeadline` | `TimeSpan?` | `null` |
| `ProtoTest:Grpc:Client:SensitiveMetadataKeys` | `GrpcClientOptions.SensitiveMetadataKeys` | `List<string>` | `authorization`, `cookie`, `set-cookie`, `x-api-key`, `api-key`, `token`, `x-auth-token`, `prototest-user` |
| `ProtoTest:Grpc:Attachments:CaptureRequestBodies` | `GrpcAttachmentOptions.CaptureRequestBodies` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:CaptureResponses` | `GrpcAttachmentOptions.CaptureResponses` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:CaptureExpectedShapes` | `GrpcAttachmentOptions.CaptureExpectedShapes` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:RedactSensitiveData` | `JsonDiagnosticOptions.RedactSensitiveData` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:MaxDiagnosticBodyLength` | `JsonDiagnosticOptions.MaxDiagnosticBodyLength` | `int` | 65536 (64 KiB) |
| `ProtoTest:Grpc:Attachments:SensitiveJsonProperties` | `JsonDiagnosticOptions.SensitiveJsonProperties` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key`, `authorization`, `cookie`, `connectionString`, `clientSecret`, `client_secret`, `id_token` |

- One `ProtoTest:Grpc:Client` section serves every named client, and each registration binds it over its code callback. A client's `configure` callback applies to that client only. The fallback client reads the shared section without any named callback.
- The older `ProtoTest:Grpc` section still binds as a fallback, as [Migrating from 1.0](../../getting-started/migrating-from-1-0.md) explains.
- `GrpcAttachmentOptions` derives from `ProtoDiagnosticCaptureOptions`, the capture options the HTTP attachment options also build on, and binds `ProtoTest:Grpc:Attachments`. There is one global attachment section per registration, not one per client.
- `GrpcClientOptions.ConfigureMetadata` is a delegate and is not bindable. A call whose `configure` throws leaves no guard behind.
- `CaptureAttachments` callbacks compose. Every callback runs in registration order, and the known section binds over the result.

## In the trace and coverage

```
grpc.call "gRPC · billing.Orders/GetOrder"   (operation: the call)
 ├─ assert.json.shape                        (child: the MatchShape assertion)
 ├─ grpc.response                            (observation → service/method coverage)
 └─ grpc-{client}-{service}-{method}-request/response-{n}  (attachments: the messages as JSON)
```

The async helpers record a `grpc.call` operation named `gRPC · {method.FullName}` with the client entity `client:ProtoTest.Grpc.ProtoGrpcClient:{name}`. Its attributes are `rpc.system`, `rpc.service`, `rpc.method`, `client.name`, `rpc.deadline`, `rpc.metadata.{key}` (sensitive values `(redacted)`), `auth.outcome`/`auth.type`, `grpc.request.count` for client streaming and `grpc.response.count`.

Request and response sections are protobuf code. A failure adds `rpc.grpc.status_code`/`rpc.grpc.status` and a `Status` Fields section with `code` and `detail`.

Each call records a `grpc.response` observation with `rpc.system`, `rpc.service`, `rpc.method` and `rpc.grpc.status`. A failed call records `grpc.failure` instead, so a call that never succeeded does not count as covered.

The client is state, not history. It appears once with `client.name`, `client.protocol`, `client.type`, `client.endpoint_source` and the sanitized `client.address`. Core adds the `client.initialize` operation and the `client.initializer` field. The `grpc.client.resolve` event records a fallback resolution, and `grpc.attachment.failed` records a capture failure with `attachment.name`.

## Skip

`AddGrpc` registers a `gRPC` capability with kind `protocol`, so the specific guard is:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "gRPC")]
```

`[RequiresInProcess]` guards tests that need the in-process transport (see [skip conditions](../../foundation/skip-conditions.md)).

## Limits

| Limit | Matters when |
| --- | --- |
| `Open*` calls report a missed in-process deadline as the transport's abort | asserting `DeadlineExceeded` on a raw call in-process. The awaited helpers report `DeadlineExceeded` on every transport. |
| Streaming capture keeps the first 10 messages | reading full streams from attachments. The cap is a private constant and not configurable. |
| Raw helpers are untraced and uncaptured | driving `Blocking` or `Open*` calls. Only `[Auth]` metadata is applied. |
| Address-dependent authenticators stay HTTP-only | applying `ApiKeyAuthenticator` with `ApiKeyLocation.Query` to gRPC. Metadata has no URI, so use a header-location key or `ConfigureMetadata`. |
| Trace sections use protobuf text format, attachments and shapes use JSON | comparing trace output with attachment content |
| One global attachment section per registration | expecting per-client attachment settings. There is no per-client section. |
| No retries | expecting backoff. There is no client interceptor beyond metadata. |

## Links

- The gRPC client registration and a real round trip: [`tests/ProtoTest.Grpc.Tests`](https://github.com/MSeys/ProtoTest/tree/main/tests/ProtoTest.Grpc.Tests).
- Related: [Coverage and observations](../../observability/coverage.md), [ProtoTrace](../../observability/prototrace.md), [Shape matching](../../foundation/shape-matching.md), [Skip conditions](../../foundation/skip-conditions.md).
