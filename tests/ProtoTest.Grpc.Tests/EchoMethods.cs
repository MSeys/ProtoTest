namespace ProtoTest.Grpc.Tests;

using global::Grpc.Core;
using Google.Protobuf;
using ProtoTest.Grpc.Tests.Echo;

/// <summary>The echo service's call descriptors, shared by the gRPC tests.</summary>
internal static class EchoMethods
{
    private static readonly Marshaller<EchoRequest> RequestMarshaller = Marshallers.Create<EchoRequest>(
        (request, context) => context.Complete(request.ToByteArray()),
        context => EchoRequest.Parser.ParseFrom(context.PayloadAsNewBuffer()));

    private static readonly Marshaller<EchoReply> ReplyMarshaller = Marshallers.Create<EchoReply>(
        (reply, context) => context.Complete(reply.ToByteArray()),
        context => EchoReply.Parser.ParseFrom(context.PayloadAsNewBuffer()));

    public static readonly Method<EchoRequest, EchoReply> Say = new(
        MethodType.Unary, "prototest.echo.Echo", "Say", RequestMarshaller, ReplyMarshaller);

    public static readonly Method<EchoRequest, EchoReply> Stream = new(
        MethodType.ServerStreaming, "prototest.echo.Echo", "Stream", RequestMarshaller, ReplyMarshaller);

    public static readonly Method<EchoRequest, EchoReply> Collect = new(
        MethodType.ClientStreaming, "prototest.echo.Echo", "Collect", RequestMarshaller, ReplyMarshaller);

    public static readonly Method<EchoRequest, EchoReply> Chat = new(
        MethodType.DuplexStreaming, "prototest.echo.Echo", "Chat", RequestMarshaller, ReplyMarshaller);
}
