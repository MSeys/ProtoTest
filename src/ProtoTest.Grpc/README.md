# ProtoTest.Grpc

Named gRPC channels with helpers for unary and streaming calls, authentication, assertions and method coverage.

```bash
dotnet add package ProtoTest.Grpc
```

```csharp
var reply = await Proto.Context.Grpc().UnaryAsync(
    Projects.GetProject,
    new GetProjectRequest { ProjectId = 42 });

reply.ShouldMatchShape(Proto.Context, new { id = 42, status = "Active" });
```

Requests, responses and status checks are recorded in the test trace. Streaming capture keeps only a bounded number of messages.

## Learn more

- [gRPC integration](https://prototest.dev/docs/integrations/grpc/)
- [Shape matching](https://prototest.dev/docs/foundation/shape-matching)
- [gRPC demo](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/GrpcJourney.cs)
