---
sidebar_position: 5
title: gRPC
description: "A named gRPC client per test for unary and streaming calls, with metadata authentication, shape and status assertions, per-call tracing and method coverage."
---

# gRPC

`ProtoTest.Grpc` gives each test a named gRPC client for unary and streaming calls, with metadata authentication through the shared `[Auth]` pipeline, per-call tracing, and service/method coverage. It follows the same client lifecycle as REST and GraphQL: the client is created during setup and recorded as a client entity, its channel is created lazily on first use, and both are released with the test.

## Install

```bash
dotnet add package ProtoTest.Grpc --prerelease
```

The package targets .NET 8, 9 and 10 (the project template defaults to `net10.0`; pass `-f net8.0` or `net9.0` for an older runtime), and brings `ProtoTest.Http` and `ProtoTest.Json` with it.

## Registering

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

The channel address comes from, in order: the explicit argument to `AddClient`, `ProtoTest:Applications:{app}:Grpc:Address`, the application's `BaseUrl`, or the application's in-process transport when the application is hosted with `AddAspNetCoreServer`. An application-scoped client with no address defers resolution to its first call. A resolver overload resolves the address per test:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api", context => context.Configuration.GetValue<Uri>("Api:Grpc")))
```

A repeated `AddGrpc` is not a no-op: its `configure` callback always runs, so more clients compose, while the lifecycle hook and the capability stay registered once. A call whose `configure` throws leaves no guard behind. `CaptureAttachments` uses `RemoveAll` + `AddSingleton`, so a repeated call replaces the options.

## Options and keys

| Key | Option | Type | Default |
| --- | --- | --- | --- |
| `ProtoTest:Grpc:Metadata` | `GrpcClientOptions.Metadata` | `IDictionary<string, string>` | empty |
| `ProtoTest:Grpc:DefaultDeadline` | `GrpcClientOptions.DefaultDeadline` | `TimeSpan?` | `null` |
| `ProtoTest:Grpc:SensitiveMetadataKeys` | `GrpcClientOptions.SensitiveMetadataKeys` | `List<string>` | `authorization`, `cookie`, `set-cookie`, `x-api-key`, `api-key`, `token`, `x-auth-token` |
| `ProtoTest:Grpc:Attachments:CaptureRequestBodies` | `ProtoHttpAttachmentOptions.CaptureRequestBodies` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:CaptureResponses` | `ProtoHttpAttachmentOptions.CaptureResponses` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:CaptureExpectedShapes` | `ProtoHttpAttachmentOptions.CaptureExpectedShapes` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:SensitiveHeaders` | `ProtoHttpAttachmentOptions.SensitiveHeaders` | `List<string>` | `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, `X-Api-Key` |
| `ProtoTest:Grpc:Attachments:SensitiveQueryParameters` | `ProtoHttpAttachmentOptions.SensitiveQueryParameters` | `List<string>` | `access_token`, `refresh_token`, `token`, `apiKey`, `api_key`, `key` |
| `ProtoTest:Grpc:Attachments:RedactSensitiveData` | `JsonDiagnosticOptions.RedactSensitiveData` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:MaxDiagnosticBodyLength` | `JsonDiagnosticOptions.MaxDiagnosticBodyLength` | `int` | 65536 (64 KiB) |
| `ProtoTest:Grpc:Attachments:SensitiveJsonProperties` | `JsonDiagnosticOptions.SensitiveJsonProperties` | `List<string>` | `password`, `token`, `access_token`, `refresh_token`, `secret`, `apiKey`, `api_key` |

One `ProtoTest:Grpc` section serves every named client; each registration binds it over its code callback. `GrpcAttachmentOptions` derives from the shared HTTP attachment options and binds `ProtoTest:Grpc:Attachments`, so there is one global attachment section per registration, not one per client. `GrpcClientOptions.ConfigureMetadata` is a delegate and is not bindable.

## API

```csharp
public static ProtoGrpcClient Grpc(this ProtoExecutionContext context, string? clientName = null);
```

Inside an `[Application]` the default or bound client is used unless a name is given, exactly like `Rest()` and `GraphQL()`; resolution tries the explicit name, then the application-qualified name (`{application}:{name}`). When no initialized client matches, the application's in-process transport backs a fallback client, registered so every call shares one channel, and a `grpc.client.resolve` event is written; otherwise the accessor throws naming the client, `AddAspNetCoreServer`, and `ProtoTest:Applications:{application}:Grpc:Address`. A call that reaches the channel with no address to resolve throws `InvalidOperationException`; a call after the client was disposed throws `ObjectDisposedException`.

The call helpers, all constrained to `where TRequest : class, TResponse : class`:

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

`ServerStreamingAsync` reads the stream to completion and returns the messages; `ClientStreamingAsync` writes every request and completes the stream before reading the response. The raw helpers return the call object untouched for callers that drive it:

```csharp
AsyncServerStreamingCall<TResponse> ServerStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method,
    TRequest request, Action<Metadata>? metadata = null, DateTime? deadline = null);           // blocks

Task<AsyncServerStreamingCall<TResponse>> OpenServerStreamingAsync<TRequest, TResponse>(
    Method<TRequest, TResponse> method, TRequest request, Action<Metadata>? metadata = null,
    DateTime? deadline = null, CancellationToken cancellationToken = default);

AsyncDuplexStreamingCall<TRequest, TResponse> DuplexStreaming<TRequest, TResponse>(
    Method<TRequest, TResponse> method, Action<Metadata>? metadata = null, DateTime? deadline = null); // blocks

Task<AsyncDuplexStreamingCall<TRequest, TResponse>> OpenDuplexStreamingAsync<TRequest, TResponse>(
    Method<TRequest, TResponse> method, Action<Metadata>? metadata = null, DateTime? deadline = null,
    CancellationToken cancellationToken = default);
```

`ServerStreaming` and `DuplexStreaming` block the calling thread while authenticators and the channel are prepared; prefer the `Open*Async` variants on a synchronizing runner. The raw helpers apply the test's `[Auth]` metadata but are untraced and uncaptured.

### Assertions

A reply is a protobuf message, and `ShouldMatchShape` matches it with the same [shapes](../../foundation/shape-matching.md) as REST and GraphQL:

```csharp
public static void ShouldMatchShape<TResponse>(this TResponse response, object expectedShape,
    JsonSerializerOptions? options = null) where TResponse : IMessage;

public static void ShouldMatchShape<TResponse>(this TResponse response, ProtoExecutionContext context,
    object expectedShape, JsonSerializerOptions? options = null) where TResponse : IMessage;
```

The reply is compared through its JSON form: field names are camelCase, enums are their names, and fields left at their default value are still present, so `quantity = 0` can be asserted. Every mismatch is reported at once. The context overload records an `assert.json.shape` operation with a `grpc.contract.shape` observation; without it, the reply is still matched, untraced.

A failed call throws `RpcException`, and both polarities of the status assertion exist, each with a context overload:

```csharp
public static RpcException ShouldHaveStatus(this RpcException exception, StatusCode expected);
public static RpcException ShouldHaveStatus(this RpcException exception, StatusCode expected,
    ProtoExecutionContext? context = null);
public static RpcException ShouldNotHaveStatus(this RpcException exception, StatusCode unexpected);
public static RpcException ShouldNotHaveStatus(this RpcException exception, StatusCode unexpected,
    ProtoExecutionContext? context = null);
```

```csharp
try
{
    await Proto.Context.Grpc().UnaryAsync(Orders.GetOrder, new GetOrderRequest { Id = 404 });
    Assert.Fail("Expected the call to fail.");
}
catch (RpcException exception)
{
    exception.ShouldHaveStatus(StatusCode.NotFound, Proto.Context);
    exception.ShouldNotHaveStatus(StatusCode.Internal, Proto.Context);
}
```

With the test context the assertion records an `assert.grpc.status` operation with expected and actual `rpc.grpc.status_code`/`rpc.grpc.status` and a `Result` Checks section; a mismatch throws `GrpcAssertionException`. Without the context the assertion is still made, untraced. gRPC deliberately exposes these as methods rather than REST's `Should`/`ShouldNot` facade, because C# has no extension properties.

## Quick start

```csharp
[Application("Api", "Grpc:Api")]
public sealed class OrderTests
{
    [ProtoTest]
    public async Task GetOrder()
    {
        var reply = await Proto.Context.Grpc().UnaryAsync(
            Orders.GetOrder,
            new GetOrderRequest { Id = 42 });

        reply.ShouldMatchShape(Proto.Context, new { id = 42, status = "PENDING" });
    }
}
```

## Going further

### Authentication

gRPC uses the same `[Auth]` attributes as the HTTP protocols:

```csharp
[Application("Api", "Grpc:Api")]
[Auth<BearerTokenAuthenticator>("token")]
public sealed class OrderTests
{
    // ...
}
```

Authenticators write HTTP headers as usual; the gRPC applier translates them to lowercase metadata keys. Metadata is layered client `Metadata` first, then `ConfigureMetadata`, then per-call `metadata`, with authenticators last so they can override. Sensitive metadata values are redacted in the trace; `SensitiveMetadataKeys` starts with the defaults above and entries under `ProtoTest:Grpc:SensitiveMetadataKeys` extend them. Matching is case-insensitive and by substring, so `authorization` also covers `proxy-authorization`.

### Attachments

```csharp
builder.AddGrpc(grpc => grpc
    .CaptureAttachments(options => options.MaxDiagnosticBodyLength = 16 * 1024)
    .AddClient("Api"));
```

With capture enabled, each traced call attaches its request and response messages as JSON: `grpc-{client}-{service}-{method}-{request|response}-{n}`, where `{client}` is the sanitized client name and `n` is the call's position in the client's call sequence, so repeated calls to the same method — even from two clients in one test — stay distinct. Values run through the shared redaction rules — JSON properties such as `password` and `token`, and the sensitive metadata keys — and each attachment is capped at `MaxDiagnosticBodyLength` from `ProtoTest:Grpc:Attachments`. `ClientStreamingAsync` and `ServerStreamingAsync` capture up to the first 10 streamed messages and record the total count in the attachment description. A message that cannot be serialized is reported as a `grpc.attachment.failed` event and never fails the call.

### Coverage

The async call helpers record a `grpc.response` observation per call. Register the collector on the target returned by `AddClient` to aggregate service/method coverage:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api")
    .AddCollector<GrpcCoverageCollector>())
```

`GrpcCoverageCollector` has category `gRPC` and counts one hit per `grpc.response` observation, identified by `{service}/{method}` (see [coverage](../../observability/coverage.md)).

### Address resolution

A resolver runs per test with the test context, so an address that only exists at call time — a container started by the suite, a per-test tenant host — still works:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api", context => context.Configuration.GetValue<Uri>("Api:Grpc")))
```

The resolver overloads register the client with a deferred address, so initialization succeeds even before the address is known; a resolver returning null at call time is an error. A non-absolute address passed to `AddClient` throws `ArgumentException`.

## Tracing and coverage

The async helpers record a `grpc.call` operation named `gRPC · {method.FullName}` with the client entity `client:ProtoTest.Grpc.ProtoGrpcClient:{name}` and attributes `rpc.system`, `rpc.service`, `rpc.method`, `client.name`, `rpc.deadline`, `rpc.metadata.{key}` (sensitive values `(redacted)`), `auth.outcome`/`auth.type`, `grpc.request.count` for client streaming and `grpc.response.count`. Request and response sections are protobuf code; a failure adds `rpc.grpc.status_code`/`rpc.grpc.status` and a `Status` Fields section with `code` and `detail`. Each call records a `grpc.response` observation with `rpc.system`, `rpc.service`, `rpc.method` and `rpc.grpc.status`.

The client is state, not history: it appears once with `client.name`, `client.protocol`, `client.type`, `client.endpoint_source` and the sanitized `client.address`; Core adds the `client.initialize` operation and the `client.initializer` field. The `grpc.client.resolve` event records a fallback resolution, and `grpc.attachment.failed` records a capture failure with `attachment.name`.

## Skip

`AddGrpc` registers a `gRPC` capability with kind `protocol`, so the specific guard is:

```csharp
[RequiresCapability(ProtoCapabilityKinds.Protocol, CapabilityName = "gRPC")]
```

`[RequiresInProcess]` guards tests that need the in-process transport (see [skip conditions](../../foundation/skip-conditions.md)).

## Limits

- **Streaming capture is capped.** Only the first 10 messages of a client- or server-streaming call are attached; the cap is a private constant and not configurable.
- **Raw helpers are untraced and uncaptured.** `ServerStreaming`, `DuplexStreaming`, `OpenServerStreamingAsync` and `OpenDuplexStreamingAsync` return the call for you to drive; only `[Auth]` metadata is applied.
- **Two serializations.** Trace request/response sections use protobuf text format, while attachments and shape matching use JSON.
- **One attachments section.** `GrpcAttachmentOptions` binds one global section per registration; there is no per-client section.
- **No retries.** There is no exponential backoff and no client interceptor beyond metadata.

## Links

- The demo's full journey: [`samples/ProtoTest.Demo/GrpcJourney.cs`](../../../../samples/ProtoTest.Demo/GrpcJourney.cs) and the host wiring in [`samples/ProtoTest.Demo/Setup.cs`](../../../../samples/ProtoTest.Demo/Setup.cs).
- Related: [Coverage and observations](../../observability/coverage.md), [ProtoTrace](../../observability/prototrace.md), [Shape matching](../../foundation/shape-matching.md), [Skip conditions](../../foundation/skip-conditions.md).
