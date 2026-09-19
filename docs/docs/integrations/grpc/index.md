---
sidebar_position: 5
title: gRPC
description: "A named gRPC client per test for unary and streaming calls, with metadata authentication, shape assertions, per-call tracing and method coverage."
---

# gRPC

`ProtoTest.Grpc` gives each test a named gRPC client for unary and streaming calls, with metadata authentication through the shared `[Auth]` pipeline, per-call tracing, and service/method coverage. It follows the same client lifecycle as REST and GraphQL: the client is created during setup and recorded as a client entity, its channel is created lazily on first use, and both are released with the test.

```bash
dotnet add package ProtoTest.Grpc
```

## Registering a client

```csharp
builder.AddApplication("Api", app => app
    .AddAspNetCoreServer<Program>()
    .AddGrpc(grpc => grpc.AddClient("Api")));
```

The channel address comes from, in order: the explicit argument to `AddClient`, `ProtoTest:Applications:{app}:Grpc:Address`, the application's `BaseUrl`, or the application's in-process transport when the application is hosted with `AddAspNetCoreServer`. A client can also resolve its address per test:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api", context => context.Configuration.GetValue<Uri>("Api:Grpc")))
```

## Calling

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

        Assert.That(reply.Id, Is.EqualTo(42));
    }
}
```

`UnaryAsync`, `ClientStreamingAsync`, `ServerStreamingAsync` (collects the stream), plus raw `ServerStreaming` and `DuplexStreaming` calls are available. Calls accept per-call metadata and a deadline; a default deadline and metadata can be configured on the client and layered from `ProtoTest:Grpc`.

```csharp
var replies = await Proto.Context.Grpc()
    .ServerStreamingAsync(Orders.Watch, new WatchRequest());
```

## Asserting on replies

A reply is a protobuf message, and `ShouldMatchShape` matches it with the same [shapes](../../foundation/shape-matching.md) as REST and GraphQL: declare only the fields the behaviour depends on, nested and partial, with `JsonValue` constraints where an exact value would be brittle.

```csharp
var order = await Proto.Context.Grpc().UnaryAsync(
    Orders.GetOrder,
    new GetOrderRequest { Id = 42 });

order.ShouldMatchShape(new
{
    id = 42,
    status = "PENDING",
    total = JsonValue.GreaterThan(0),
    lines = new[]
    {
        new { sku = "notebook", quantity = 2 }
    }
});
```

The reply is compared through its JSON form: field names are camelCase, enums are their names, and fields left at their default value are still present, so `quantity = 0` can be asserted. Every mismatch is reported at once, the same way a REST shape reports them. Pass the test context — `order.ShouldMatchShape(Proto.Context, new { ... })` — to record the assertion as an `assert.json.shape` operation with a `grpc.contract.shape` observation; without it, the reply is still matched, untraced.

A failed call throws `RpcException`. `ShouldHaveStatus` asserts the status it carries, like REST's `ShouldHaveHttpStatus`:

```csharp
try
{
    await Proto.Context.Grpc().UnaryAsync(Orders.GetOrder, new GetOrderRequest { Id = 404 });
    Assert.Fail("Expected the call to fail.");
}
catch (RpcException exception)
{
    exception.ShouldHaveStatus(StatusCode.NotFound, Proto.Context);
}
```

With the test context it records an `assert.grpc.status` operation with the expected and actual `rpc.grpc.status_code`/`rpc.grpc.status`, and a Checks section; a mismatch throws `GrpcAssertionException`. Without the context the assertion is still made, untraced.

## Authentication

gRPC uses the same `[Auth]` attributes as the HTTP protocols:

```csharp
[Application("Api", "Grpc:Api")]
[Auth<BearerTokenAuthenticator>("token")]
public sealed class OrderTests
{
    // ...
}
```

Authenticators write HTTP headers as usual; the gRPC applier translates them to lowercase metadata keys. `authorization` and other sensitive metadata values are redacted in the trace. Which keys are sensitive is configurable: `SensitiveMetadataKeys` starts with the defaults (`authorization`, `cookie`, `set-cookie`, `x-api-key`, `api-key`, `token`, `x-auth-token`) and entries under `ProtoTest:Grpc:SensitiveMetadataKeys` extend them. Matching is case-insensitive and by substring, so `authorization` also covers `proxy-authorization`.

## Attachments

```csharp
builder.AddGrpc(grpc => grpc
    .CaptureAttachments(options => options.MaxDiagnosticBodyLength = 16 * 1024)
    .AddClient("Api"));
```

With capture enabled, each traced call attaches its request and response messages as JSON: `grpc-{client}-{service}-{method}-request-{n}` and `grpc-{client}-{service}-{method}-response-{n}`, where `{client}` is the sanitized client name and `n` is the call's position in the client's call sequence, so repeated calls to the same method — even from two clients in one test — stay distinct; field names are camelCase. Values run through the same redaction rules as REST and GraphQL — JSON properties such as `password` and `token`, and the metadata keys above — and each attachment is capped at `MaxDiagnosticBodyLength` from `ProtoTest:Grpc:Attachments`. `ClientStreamingAsync` and `ServerStreamingAsync` capture up to the first 10 streamed messages and record the total message count in the attachment description; `ClientStreamingAsync` records the number of sent messages in `grpc.request.count` and one received response in `grpc.response.count`, while `ServerStreamingAsync` records the number of received messages in `grpc.response.count`. A message that cannot be serialized is reported as a `grpc.attachment.failed` event and never fails the call. The raw `ServerStreaming` and `DuplexStreaming` calls remain untraced and are not captured.

## Tracing and coverage

The async helpers — `UnaryAsync`, `ClientStreamingAsync` and `ServerStreamingAsync` — each record a `grpc.call` operation named `Service/Method` with `rpc.*` attributes, request/response sections, call status on failure, and `auth.outcome`/`auth.type`, and they apply `[Auth]` metadata. A `grpc.response` observation is recorded per call, so registering the collector aggregates service/method coverage:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api")
    .AddCollector<GrpcCoverageCollector>())
```

The raw `ServerStreaming` and `DuplexStreaming` calls are an escape hatch: they return the call object for you to drive, and they are **not** traced. They do apply the test's `[Auth]` metadata; pass metadata explicitly for anything else.
