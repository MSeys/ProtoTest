---
sidebar_position: 2
title: Calls and assertions
description: "Call a gRPC service with the traced unary and streaming helpers, keep the method descriptors in one holder, and assert on the reply or the status."
---

import TabbedCode from '@site/src/components/TabbedCode';

# Calls and assertions

The traced helpers cover unary, client streaming and server streaming. Anything else uses the raw calls, which only apply `[Auth]` metadata.

<TabbedCode
  label="Traced calls"
  tabs={[
    {
      id: 'unary',
      label: 'Unary',
      code: `var reply = await Proto.Context.Grpc().UnaryAsync(
    OrdersMethods.GetOrder,
    new GetOrderRequest { Id = 42 });

ProtoGrpcAssertions.For(reply).Should.MatchShape(new { id = 42, status = "PENDING" });`,
    },
    {
      id: 'server-streaming',
      label: 'Server streaming',
      code: `IReadOnlyList<GetOrderReply> replies = await Proto.Context.Grpc()
    .ServerStreamingAsync(
        OrdersMethods.ListOrders,
        new ListOrdersRequest { Status = "PENDING" });`,
    },
  ]}
/>

`OrdersMethods` is the descriptor holder below: one descriptor per method, used everywhere.

## Call helpers

All helpers are constrained to `where TRequest : class, TResponse : class`:

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

## Method descriptors

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

## Assertions

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

## Links

- [gRPC](./index.md): compose, address resolution, authentication, attachments, coverage and limits.
- [Shape matching](../../foundation/shape-matching.md): the rules `MatchShape` applies.
