# ProtoTest.Grpc

Named gRPC channels with unary and streaming helpers, metadata authentication through `[Auth<T>]`, shape and status assertions, and method coverage.

```bash
dotnet add package ProtoTest.Grpc
```

## Quick start

```csharp
builder.AddApplication("Api", app => app.AddGrpc(grpc => grpc.AddClient("Projects")));

[Application("Api", "Grpc:Projects")]
[Auth<BearerTokenAuthenticator>("token")]
public sealed class ProjectTests
{
    [ProtoTest]
    public async Task GetProject()
    {
        var reply = await Proto.Context.Grpc().UnaryAsync(
            Projects.GetProject,
            new GetProjectRequest { ProjectId = 42 });

        reply.ShouldMatchShape(Proto.Context, new
        {
            id = 42,
            status = "Active"
        });
    }
}
```

## What it adds

- **Client** — `Proto.Context.Grpc(name?)` resolves a named channel from the explicit address, `ProtoTest:Applications:{app}:Grpc:Address`, the application's `BaseUrl`, or the in-process transport.
- **Call helpers** — `UnaryAsync`, `ClientStreamingAsync`, `ServerStreamingAsync` and raw `OpenServerStreamingAsync`/`OpenDuplexStreamingAsync` (or the blocking `ServerStreaming`/`DuplexStreaming`).
- **Authentication** — the shared `[Auth<T>]` authenticators write lowercase metadata; `ProtoTest:Grpc:SensitiveMetadataKeys` controls what is redacted in the trace.
- **Assertions** — `ShouldMatchShape(context, shape)` records `assert.json.shape`, and `RpcException.ShouldHaveStatus`/`ShouldNotHaveStatus` record `assert.grpc.status`.
- **Coverage** — `.AddCollector<GrpcCoverageCollector>()` counts `{service}/{method}` calls; `grpc.response` observations carry the status.
- **Tracing** — `grpc.call` operations with protobuf request/response sections, redacted `rpc.metadata.*` and `grpc.attachment.failed` events when capture fails.

## Configuration

| Key | Type | Default |
| --- | --- | --- |
| `ProtoTest:Grpc:Metadata` | `IDictionary<string, string>` | empty |
| `ProtoTest:Grpc:DefaultDeadline` | `TimeSpan?` | `null` |
| `ProtoTest:Grpc:SensitiveMetadataKeys` | `List<string>` | `authorization`, `cookie`, `set-cookie`, `x-api-key`, `api-key`, `token`, `x-auth-token` |
| `ProtoTest:Grpc:Attachments:CaptureRequestBodies` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:CaptureResponses` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:CaptureExpectedShapes` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:RedactSensitiveData` | `bool` | `true` |
| `ProtoTest:Grpc:Attachments:MaxDiagnosticBodyLength` | `int` | `65536` |

Attachment options inherit the shared `ProtoHttpAttachmentOptions` keys, including `SensitiveHeaders`, `SensitiveQueryParameters` and `SensitiveJsonProperties`. Streaming capture keeps only the first 10 messages, and the raw streaming helpers are untraced because the caller drives the call.

## Learn more

- [gRPC guide](https://prototest.dev/docs/integrations/grpc/)
- [GrpcJourney.cs](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/GrpcJourney.cs)
