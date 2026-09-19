# ProtoTest.Grpc

Runner-independent gRPC testing for ProtoTest, with named channels, metadata authentication through the shared `[Auth]` pipeline, unary and streaming helpers, per-call tracing, and service/method coverage.

```bash
dotnet add package ProtoTest.Grpc --prerelease
```

```csharp
builder.AddApplication("Api", app => app.AddGrpc(grpc => grpc.AddClient("Api")));

[Application("Api", "Grpc:Api")]
[Auth<BearerTokenAuthenticator>("token")]
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

The channel address comes from the explicit `AddClient` argument, `ProtoTest:Applications:{app}:Grpc:Address`, the application's `BaseUrl`, or the application's in-process transport. `[Auth]` authenticators are shared with the HTTP protocols; their headers become lowercase gRPC metadata, and the keys redacted in the trace are configurable through `GrpcClientOptions.SensitiveMetadataKeys` and `ProtoTest:Grpc:SensitiveMetadataKeys`. `UnaryAsync`, `ClientStreamingAsync` and `ServerStreamingAsync` are traced as `grpc.call` spans with request/response sections and redacted metadata, and feed `GrpcCoverageCollector` when registered with `.AddCollector<GrpcCoverageCollector>()`. Add `.CaptureAttachments()` to attach the request and response messages as redacted, capped JSON, named `grpc-{client}-{service}-{method}-{request|response}-{n}` so repeated calls to the same method — even from two clients in one test — stay distinct; streaming helpers capture the first 10 messages, `ClientStreamingAsync` records the number of sent messages in `grpc.request.count`, and `ServerStreamingAsync` records the number received in `grpc.response.count`. A message that cannot be serialized is reported as a `grpc.attachment.failed` event and never fails the call. The raw `ServerStreaming` and `DuplexStreaming` helpers apply `[Auth]` metadata too, but stay untraced because the caller drives the call. `ShouldMatchShape` checks a reply against the same shapes as REST and GraphQL, and the context-aware overload records the assertion as an `assert.json.shape` operation with a `grpc.contract.shape` observation. A failed `RpcException` can assert its status with `ShouldHaveStatus`, traced as an `assert.grpc.status` operation. See the [gRPC guide](https://github.com/MSeys/ProtoTest/blob/main/docs/docs/integrations/grpc/index.md).
