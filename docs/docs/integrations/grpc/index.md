---
sidebar_position: 4
title: gRPC
---

# gRPC

`ProtoTest.Grpc` gives each test a named gRPC client for unary and streaming calls, with metadata authentication through the shared `[Auth]` pipeline, per-call tracing, and service/method coverage. It follows the same client lifecycle as REST and GraphQL: the channel is created during setup, recorded as a client entity, and released with the test.

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

Authenticators write HTTP headers as usual; the gRPC applier translates them to lowercase metadata keys. `authorization` and other sensitive metadata values are redacted in the trace.

## Tracing and coverage

Every call is a `grpc.call` operation named `Service/Method` with `rpc.*` attributes, request/response sections, call status on failure, and `auth.outcome`/`auth.type`. A `grpc.response` observation is recorded per call, so registering the collector aggregates service/method coverage:

```csharp
.AddGrpc(grpc => grpc.AddClient("Api")
    .AddCollector<GrpcCoverageCollector>())
```
