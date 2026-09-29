---
sidebar_position: 5
title: gRPC
description: "A named gRPC client per test for unary and streaming calls, with metadata authentication, shape and status assertions, per-call tracing and method coverage."
---

# gRPC

## What it adds

`ProtoTest.Grpc` gives each test a named gRPC client for unary and streaming calls, with metadata authentication through the shared `[Auth]` pipeline, per-call tracing, and service/method coverage. It follows the same client lifecycle as REST and GraphQL: the client is created during setup and recorded as a client entity, its channel is created lazily on first use, and both are released with the test.

## Install

```bash
dotnet add package ProtoTest.Grpc
```

The package supports .NET 8, 9 and 10. It depends on `ProtoTest.Http` and `ProtoTest.Json`. See [Installation](../../getting-started/installation.md) for the supported .NET versions.

## Compose

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddGrpc(grpc => grpc.AddClient("Api")));
```

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

The channel address comes from, in order: the explicit argument to `AddClient`, `ProtoTest:Applications:{app}:Grpc:Address`, the application's `BaseUrl`, or the application's in-process transport when the application is hosted with `AddAspNetCoreServer`. Both application keys resolve with the shared precedence - an address a started piece published wins over configuration - so the client follows the process the run started. An application-scoped client with no address defers resolution to its first call. A resolver overload resolves the address per test:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api", context => context.Configuration.GetValue<Uri>("Api:Grpc")))
```

Calling `AddGrpc` twice does not throw. The shared setup runs once. Each callback still adds its clients. A call whose `configure` throws leaves no guard behind. `CaptureAttachments` callbacks also compose: every callback runs in registration order and the known section binds over the result.

### Options and keys

| Key | Option | Type | Default |
| --- | --- | --- | --- |
| `ProtoTest:Grpc:Client:Metadata` | `GrpcClientOptions.Metadata` | `IDictionary<string, string>` | empty |
| `ProtoTest:Grpc:Client:DefaultDeadline` | `GrpcClientOptions.DefaultDeadline` | `TimeSpan?` | `null` |
| `ProtoTest:Grpc:Client:SensitiveMetadataKeys` | `GrpcClientOptions.SensitiveMetadataKeys` | `List<string>` | `authorization`, `cookie`, `set-cookie`, `x-api-key`, `api-key`, `token`, `x-auth-token`, `prototest-user` |
| `ProtoTest:Grpc:Attachments:CaptureRequestBodies` | `ProtoHttpAttachmentOptions.CaptureRequestBodies` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:CaptureResponses` | `ProtoHttpAttachmentOptions.CaptureResponses` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:CaptureExpectedShapes` | `ProtoHttpAttachmentOptions.CaptureExpectedShapes` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:RedactSensitiveData` | `JsonDiagnosticOptions.RedactSensitiveData` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:MaxDiagnosticBodyLength` | `JsonDiagnosticOptions.MaxDiagnosticBodyLength` | `int` | 65536 (64 KiB) |
| `ProtoTest:Grpc:Attachments:SensitiveJsonProperties` | `JsonDiagnosticOptions.SensitiveJsonProperties` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key`, `authorization`, `cookie`, `connectionString`, `clientSecret` |

One `ProtoTest:Grpc:Client` section serves every named client; each registration binds it over its code callback. A client's `configure` callback applies to that client only. The fallback client reads the shared section without any named callback. The older `ProtoTest:Grpc` section still binds as a fallback; see [Migrating from 1.0](../../getting-started/migrating-from-1-0.md). `GrpcAttachmentOptions` derives from the shared HTTP attachment options and binds `ProtoTest:Grpc:Attachments`, so there is one global attachment section per registration, not one per client. `GrpcClientOptions.ConfigureMetadata` is a delegate and is not bindable.

### API

```csharp
public static ProtoGrpcClient Grpc(this ProtoExecutionContext context, string? clientName = null);
```

Inside `[Application]` the test uses the bound client unless you pass a name. Resolution tries `{application}:{name}`, then the name itself. A host-registered client stays reachable from inside an application, and so is a name exactly one other application registered. A name used by two applications needs the qualified form (`App:Client`). A miss lists the registered gRPC client names.

When no initialized client matches, the application's in-process transport backs a fallback client, registered so every call shares one channel, and a `grpc.client.resolve` event is written. Otherwise the accessor throws naming the client, `AddAspNetCoreServer`, and `ProtoTest:Applications:{application}:Grpc:Address`. A call that reaches the channel with no address to resolve throws `InvalidOperationException`; a call after the client was disposed throws `ObjectDisposedException`.

```
explicit AddClient address? → ProtoTest:Applications:{app}:Grpc:Address? → app BaseUrl?
  → in-process transport? → else fallback client + grpc.client.resolve event
resolver overload → deferred to first call (null at call time is an error)
```

The call helpers, all constrained to `where TRequest : class, TResponse : class`:

| Helper | Requests | Responses | Returns | Traced |
| --- | --- | --- | --- | --- |
| `UnaryAsync` | 1 | 1 | `TResponse` | yes |
| `ClientStreamingAsync` | N | 1 | `TResponse` | yes, first 10 messages attached |
| `ServerStreamingAsync` | 1 | N | message list | yes, first 10 messages attached |
| `Blocking.ServerStreaming`, `Blocking.DuplexStreaming` | … | … | call to drive | no, auth only |
| `OpenServerStreamingAsync`, `OpenDuplexStreamingAsync` | … | … | call to drive | no, auth only |

Use raw calls only when the traced helpers lack the call shape you need.

```csharp
Task<TResponse> UnaryAsync<TRequest, TResponse>(Method<TRequest, TResponse> method, TRequest request,
    Action<Metadata>? metadata = null, DateTime? deadline = null, CancellationToken cancellationToken = default);

Task<TResponse> ClientStreamingAsync<TRequest, TResponse>(Method<TRequest, TResponse> method,
    IEnumerable<TRequest> requests, Action<Metadata>? metadata = null, DateTime? deadline = null,
    CancellationToken cancellationToken = default);

Task<IReadOnlyList<TResponse>> ServerStreamingAsync<TRequest, TResponse>(Method<TRequest, TResponse> method,
    TRequest request, Action<Metadata>? metadata = null, DateTime? deadline = null,
    CancellationToken cancellationToken = default);
```

`ServerStreamingAsync` reads the stream to completion and returns the messages; `ClientStreamingAsync` writes every request and completes the stream before reading the response. The raw helpers return the call object untouched for callers that drive it; the blocking two live on `client.Blocking`:

```csharp
// client.Blocking
AsyncServerStreamingCall<TResponse> ServerStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method,
    TRequest request, Action<Metadata>? metadata = null, DateTime? deadline = null);

AsyncDuplexStreamingCall<TRequest, TResponse> DuplexStreaming<TRequest, TResponse>(
    Method<TRequest, TResponse> method, Action<Metadata>? metadata = null, DateTime? deadline = null);

// client
Task<AsyncServerStreamingCall<TResponse>> OpenServerStreamingAsync<TRequest, TResponse>(
    Method<TRequest, TResponse> method, TRequest request, Action<Metadata>? metadata = null,
    DateTime? deadline = null, CancellationToken cancellationToken = default);

Task<AsyncDuplexStreamingCall<TRequest, TResponse>> OpenDuplexStreamingAsync<TRequest, TResponse>(
    Method<TRequest, TResponse> method, Action<Metadata>? metadata = null, DateTime? deadline = null,
    CancellationToken cancellationToken = default);
```

`Blocking.ServerStreaming` and `Blocking.DuplexStreaming` block the calling thread while authenticators and the channel are prepared; prefer the `Open*Async` variants on a synchronizing runner.

#### Method descriptors

The call helpers take a `Method<TRequest, TResponse>`; the generated client classes do not expose one per method, so keep the suite's call descriptors in one holder and use it everywhere:

```csharp
public static class OrdersMethods
{
    private static readonly Marshaller<GetOrderRequest> RequestMarshaller = Marshallers.Create<GetOrderRequest>(
        (request, context) => context.Complete(request.ToByteArray()),
        context => GetOrderRequest.Parser.ParseFrom(context.PayloadAsNewBuffer()));

    private static readonly Marshaller<GetOrderReply> ReplyMarshaller = Marshallers.Create<GetOrderReply>(
        (reply, context) => context.Complete(reply.ToByteArray()),
        context => GetOrderReply.Parser.ParseFrom(context.PayloadAsNewBuffer()));

    public static readonly Method<GetOrderRequest, GetOrderReply> GetOrder =
        new(MethodType.Unary, "billing.Orders", "GetOrder", RequestMarshaller, ReplyMarshaller);
}
```

The `MethodType` is `Unary`, `ServerStreaming`, `ClientStreaming` or `DuplexStreaming`, and the service name is the proto's fully qualified one (`package.Service`), so the descriptor addresses the same route the generated client uses.

#### Assertions

A reply is a protobuf message, and `Should.MatchShape` matches it with the same [shapes](../../foundation/shape-matching.md) as REST and GraphQL. A message reaches its facade through the factory; `MatchShape` returns the reply:

```csharp
public static ProtoGrpcMessageAssertions<TResponse> For<TResponse>(TResponse response)
    where TResponse : IMessage;
// ProtoGrpcMessageAssertions<TResponse>: Should -> ProtoGrpcMessageAssertions<TResponse>
//                                       MatchShape(object expectedShape, JsonSerializerOptions? options = null) -> TResponse
//                                       MatchShape(object expectedShape, bool exact, JsonSerializerOptions? options = null) -> TResponse
```

The reply is compared through its JSON form: field names are camelCase, enums are their names, and fields left at their default value are still present, so `quantity = 0` can be asserted. Every mismatch is reported at once. The assertion records an `assert.json.shape` operation with a `grpc.contract.shape` observation on the ambient test context, like the other integrations' data-object assertions. A mismatch throws `GrpcAssertionException` whose message starts with the message type, keeping the shared `JsonShapeMismatchException` as `InnerException`. The older `ShouldMatchShape` spelling is listed in [Migrating from 1.0](../../getting-started/migrating-from-1-0.md).

Keep one copy of the exact-mode rule on the [shape matching page](../../foundation/shape-matching.md#exact-matching): exact mode flags any unlisted field.

A failed call throws `RpcException`; `ProtoGrpcAssertions.For(exception)` returns its assertion facade:

```csharp
public static ProtoGrpcExceptionAssertions For(RpcException exception);
// ProtoGrpcExceptionAssertions: Should / ShouldNot -> ProtoGrpcExceptionAssertions
//                               HaveStatus(StatusCode expected) -> RpcException
```

```csharp
try
{
    await Proto.Context.Grpc().UnaryAsync(OrdersMethods.GetOrder, new GetOrderRequest { Id = 404 });
    Assert.Fail("Expected the call to fail.");
}
catch (RpcException exception)
{
    ProtoGrpcAssertions.For(exception).Should.HaveStatus(StatusCode.NotFound);
    ProtoGrpcAssertions.For(exception).ShouldNot.HaveStatus(StatusCode.Internal);
}
```

The assertion records an `assert.grpc.status` operation on the ambient test context with expected and actual `rpc.grpc.status_code`/`rpc.grpc.status` and a `Result` Checks section; a mismatch throws `GrpcAssertionException`. The facade goes through `For(...)` because C# has no extension properties. The older `ShouldHaveStatus`/`ShouldNotHaveStatus` spellings are listed in [Migrating from 1.0](../../getting-started/migrating-from-1-0.md).

## The tasks

```csharp
[Application("Api", "Grpc:Api")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task GetOrder()
    {
        // OrdersMethods is the descriptor holder from "Method descriptors" above.
        var reply = await Proto.Context.Grpc().UnaryAsync(
            OrdersMethods.GetOrder,
            new GetOrderRequest { Id = 42 });

        ProtoGrpcAssertions.For(reply).Should.MatchShape(new { id = 42, status = "PENDING" });
    }
}
```

Run it with `dotnet test`. A green run prints the passed test, and the trace lands at `TestResults/prototest-{runId}.prototrace` with a `grpc.call` operation for the call.

### Going further

#### Authentication

gRPC uses the same `[Auth]` attributes as the HTTP protocols - including the
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

Authenticators write HTTP headers as usual; the gRPC applier translates them to lowercase metadata keys. Metadata is layered client `Metadata` first, then `ConfigureMetadata`, then per-call `metadata`, with authenticators last so they can override. Each call resolves its authenticator from the test's factory, so a stateful authenticator is created per call and never shared between calls. Sensitive metadata values are redacted in the trace; `SensitiveMetadataKeys` starts with the defaults above and entries under `ProtoTest:Grpc:Client:SensitiveMetadataKeys` extend them. Matching is case-insensitive and by substring, so `authorization` also covers `proxy-authorization`.

#### Attachments

```csharp
builder.AddGrpc(grpc => grpc
    .CaptureAttachments(options => options.MaxDiagnosticBodyLength = 16 * 1024)
    .AddClient("Api"));
```

With capture enabled, each traced call attaches its request and response messages as JSON: `grpc-{client}-{service}-{method}-{request|response}-{n}`, where `{client}` is the sanitized client name and `n` is the call's position in the client's call sequence, so repeated calls to the same method stay distinct, even from two clients in one test. Values run through the shared redaction rules, covering JSON properties such as `password` and `token` and the sensitive metadata keys, and each attachment is capped at `MaxDiagnosticBodyLength` from `ProtoTest:Grpc:Attachments`. `ClientStreamingAsync` and `ServerStreamingAsync` capture up to the first 10 streamed messages and record the total count in the attachment description. A message that cannot be serialized is reported as a `grpc.attachment.failed` event and never fails the call.

#### Coverage

The async call helpers record a `grpc.response` observation per successful call. Register the collector on the target returned by `AddClient` to aggregate service/method coverage:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api")
    .AddCollector<GrpcCoverageCollector>())
```

`GrpcCoverageCollector` has category `gRPC` and counts one hit per `grpc.response` observation, identified by `{service}/{method}` (see [coverage](../../observability/coverage.md)).

#### Address resolution

A resolver runs per test with the test context, so an address that only exists at call time, such as a container started by the suite or a per-test tenant host, still works:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api", context => context.Configuration.GetValue<Uri>("Api:Grpc")))
```

The resolver overloads register the client with a deferred address, so initialization succeeds even before the address is known; a resolver returning null at call time is an error. A non-absolute address passed to `AddClient` throws `ArgumentException`.

## In the trace and coverage

The async helpers record a `grpc.call` operation named `gRPC · {method.FullName}` with the client entity `client:ProtoTest.Grpc.ProtoGrpcClient:{name}` and attributes `rpc.system`, `rpc.service`, `rpc.method`, `client.name`, `rpc.deadline`, `rpc.metadata.{key}` (sensitive values `(redacted)`), `auth.outcome`/`auth.type`, `grpc.request.count` for client streaming and `grpc.response.count`. Request and response sections are protobuf code; a failure adds `rpc.grpc.status_code`/`rpc.grpc.status` and a `Status` Fields section with `code` and `detail`. Each call records a `grpc.response` observation with `rpc.system`, `rpc.service`, `rpc.method` and `rpc.grpc.status`; a failed call records `grpc.failure` instead, so a call that never succeeded does not count as covered.

The client is state, not history: it appears once with `client.name`, `client.protocol`, `client.type`, `client.endpoint_source` and the sanitized `client.address`; Core adds the `client.initialize` operation and the `client.initializer` field. The `grpc.client.resolve` event records a fallback resolution, and `grpc.attachment.failed` records a capture failure with `attachment.name`.

## Skip

`AddGrpc` registers a `gRPC` capability with kind `protocol`, so the specific guard is:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "gRPC")]
```

`[RequiresInProcess]` guards tests that need the in-process transport (see [skip conditions](../../foundation/skip-conditions.md)).

## Limits

| Limit | Matters when |
| --- | --- |
| A missed deadline can report the transport's abort in-process | asserting `DeadlineExceeded` against both socket and in-process endpoints; assert it against socket endpoints only, or accept either status |
| Streaming capture keeps the first 10 messages | reading full streams from attachments; the cap is a private constant and not configurable |
| Raw helpers are untraced and uncaptured | driving `Blocking` or `Open*` calls; only `[Auth]` metadata is applied |
| Address-dependent authenticators stay HTTP-only | applying `ApiKeyAuthenticator` with `ApiKeyLocation.Query` to gRPC; metadata has no URI, so use a header-location key or `ConfigureMetadata` |
| Trace sections use protobuf text format, attachments and shapes use JSON | comparing trace output with attachment content |
| One global attachment section per registration | expecting per-client attachment settings; there is no per-client section |
| No retries | expecting backoff; there is no client interceptor beyond metadata |

## Links

- The gRPC client registration and a real round trip: [`tests/ProtoTest.Grpc.Tests`](https://github.com/MSeys/ProtoTest/tree/main/tests/ProtoTest.Grpc.Tests).
- Related: [Coverage and observations](../../observability/coverage.md), [ProtoTrace](../../observability/prototrace.md), [Shape matching](../../foundation/shape-matching.md), [Skip conditions](../../foundation/skip-conditions.md).
