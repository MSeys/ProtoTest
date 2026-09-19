namespace ProtoTest.Grpc.Tests.Echo;

using global::Grpc.Core;

/// <summary>A minimal service the integration tests drive: echoes, streams on commas, and records auth.</summary>
public sealed class EchoService : Echo.EchoBase
{
    public static string? LastAuthorization { get; private set; }
    public static string? LastStreamAuthorization { get; private set; }

    public override Task<EchoReply> Say(EchoRequest request, ServerCallContext context)
    {
        LastAuthorization = context.RequestHeaders.GetValue("authorization");
        return Task.FromResult(new EchoReply { Message = request.Message, Password = request.Password });
    }

    public override async Task Stream(
        EchoRequest request,
        IServerStreamWriter<EchoReply> responseStream,
        ServerCallContext context)
    {
        LastStreamAuthorization = context.RequestHeaders.GetValue("authorization");
        foreach (var part in request.Message.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            await responseStream.WriteAsync(new EchoReply { Message = part.Trim() });
        }
    }

    public override async Task<EchoReply> Collect(
        IAsyncStreamReader<EchoRequest> requestStream,
        ServerCallContext context)
    {
        var parts = new List<string>();
        await foreach (var request in requestStream.ReadAllAsync())
        {
            parts.Add(request.Message);
        }

        return new EchoReply { Message = string.Join("+", parts) };
    }

    public override async Task Chat(
        IAsyncStreamReader<EchoRequest> requestStream,
        IServerStreamWriter<EchoReply> responseStream,
        ServerCallContext context)
    {
        await foreach (var request in requestStream.ReadAllAsync())
        {
            await responseStream.WriteAsync(new EchoReply { Message = request.Message.ToUpperInvariant() });
        }
    }
}
