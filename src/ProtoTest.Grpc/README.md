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

The channel address comes from the explicit `AddClient` argument, `ProtoTest:Applications:{app}:Grpc:Address`, the application's `BaseUrl`, or the application's in-process transport. `[Auth]` authenticators are shared with the HTTP protocols; their headers become lowercase gRPC metadata. Calls appear as `grpc.call` spans with request/response sections and redacted metadata, and feed `GrpcCoverageCollector` when registered with `.AddCollector<GrpcCoverageCollector>()`. See the [gRPC guide](https://github.com/matthiasseys/ProtoTest/blob/main/docs/integrations/grpc.md).
