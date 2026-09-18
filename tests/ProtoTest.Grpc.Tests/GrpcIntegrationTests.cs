namespace ProtoTest.Grpc.Tests;

using System.Net;
using System.Net.Sockets;
using System.Reflection;
using global::Grpc.Core;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Grpc.Tests.Echo;
using ProtoTest.Http;
using ProtoTest.Http.Authenticators;

[TestFixture]
public sealed class GrpcIntegrationTests
{
    private static readonly Marshaller<EchoRequest> RequestMarshaller = Marshallers.Create<EchoRequest>(
        (request, context) => context.Complete(request.ToByteArray()),
        context => EchoRequest.Parser.ParseFrom(context.PayloadAsNewBuffer()));
    private static readonly Marshaller<EchoReply> ReplyMarshaller = Marshallers.Create<EchoReply>(
        (reply, context) => context.Complete(reply.ToByteArray()),
        context => EchoReply.Parser.ParseFrom(context.PayloadAsNewBuffer()));
    private static readonly Method<EchoRequest, EchoReply> SayMethod = new(
        MethodType.Unary, "prototest.echo.Echo", "Say", RequestMarshaller, ReplyMarshaller);
    private static readonly Method<EchoRequest, EchoReply> StreamMethod = new(
        MethodType.ServerStreaming, "prototest.echo.Echo", "Stream", RequestMarshaller, ReplyMarshaller);
    private static readonly Method<EchoRequest, EchoReply> CollectMethod = new(
        MethodType.ClientStreaming, "prototest.echo.Echo", "Collect", RequestMarshaller, ReplyMarshaller);
    private static readonly Method<EchoRequest, EchoReply> ChatMethod = new(
        MethodType.DuplexStreaming, "prototest.echo.Echo", "Chat", RequestMarshaller, ReplyMarshaller);

    private static WebApplication _server = null!;
    private static string _address = null!;

    [OneTimeSetUp]
    public async Task StartServer()
    {
        var port = FreePort();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(
            IPAddress.Loopback,
            port,
            endpoint => endpoint.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        _server = builder.Build();
        _server.MapGrpcService<EchoService>();
        await _server.StartAsync();
        _address = $"http://127.0.0.1:{port}";
    }

    [OneTimeTearDown]
    public async Task StopServer() => await _server.DisposeAsync();

    [Test]
    public async Task UnaryCall_ShouldTraceReportAndCarryMetadata()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address).AddCollector<GrpcCoverageCollector>());
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc unary", TestMethod());
        var client = context.Grpc("Echo");

        var reply = await client.UnaryAsync(
            SayMethod,
            new EchoRequest { Message = "hello" },
            metadata => metadata.Add("authorization", "Bearer secret"));

        // Resolve coverage before the test completes: the test scope is disposed with the result.
        var coverage = context.Services.GetServices<IProtoCollector>()
            .OfType<GrpcCoverageCollector>()
            .Single()
            .GetReportItems()
            .Single();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(reply.Message, Is.EqualTo("hello"));
            Assert.That(EchoService.LastAuthorization, Is.EqualTo("Bearer secret"));
            var call = test.Entries.Single(entry => entry.Kind == "grpc.call");
            Assert.That(call.Outcome, Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(call.Attributes["rpc.service"], Is.EqualTo("prototest.echo.Echo"));
            Assert.That(call.Attributes["rpc.metadata.authorization"], Is.EqualTo("(redacted)"));
            Assert.That(call.Attributes["auth.outcome"], Is.EqualTo("skipped"));
            Assert.That(call.Sections!.Any(section => section.Label == "Response"), Is.True);
            Assert.That(
                context.RecordedObservations.Any(observation => observation.Kind == "grpc.response"),
                Is.True);

            // The registered collector turns those observations into service/method coverage.
            Assert.That(coverage.Identifier, Is.EqualTo("prototest.echo.Echo/Say"));
            Assert.That(coverage.Category, Is.EqualTo("gRPC"));
            Assert.That(coverage.IsCovered, Is.True);
            Assert.That(coverage.Count, Is.EqualTo(1));

            // Core initializes the client during setup, like every other integration's client.
            Assert.That(
                test.Entries.Any(entry => entry.Kind == "client.initialize" && entry.Name.Contains("ProtoGrpcClient")),
                Is.True);
            var entity = test.Entities!.Single(candidate =>
                candidate.Kind == ProtoTraceEntityKinds.Client && candidate.Id.Contains("ProtoGrpcClient"));
            Assert.That(entity.State["client.address"], Does.StartWith(_address));
            Assert.That(entity.State["client.protocol"], Is.EqualTo("Grpc"));
            Assert.That(entity.State["client.initializer"], Is.EqualTo("ProtoGrpcClientInitializer"));
        });
    }

    [Test]
    public async Task AuthAttribute_ShouldApplyMetadataThroughTheSharedPipeline()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc auth", AuthenticatedTestMethod());
        var client = context.Grpc("Echo");

        var reply = await client.UnaryAsync(SayMethod, new EchoRequest { Message = "auth" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var call = host.Trace.Snapshot().Tests.Single().Entries.Single(entry => entry.Kind == "grpc.call");
        Assert.Multiple(() =>
        {
            Assert.That(reply.Message, Is.EqualTo("auth"));
            Assert.That(EchoService.LastAuthorization, Is.EqualTo("Bearer shared-token"));
            Assert.That(call.Attributes["auth.outcome"], Is.EqualTo("applied"));
            Assert.That(call.Attributes["rpc.metadata.authorization"], Is.EqualTo("(redacted)"));
        });
    }

    [Test]
    public async Task ServerStreaming_ShouldReturnEveryMessage()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc stream", TestMethod());
        var client = context.Grpc("Echo");

        var replies = await client.ServerStreamingAsync(StreamMethod, new EchoRequest { Message = "a, b, c" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(replies.Select(reply => reply.Message), Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public async Task ClientStreaming_ShouldCollectEveryMessage()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc client stream", TestMethod());
        var client = context.Grpc("Echo");

        var reply = await client.ClientStreamingAsync(
            CollectMethod,
            [new EchoRequest { Message = "x" }, new EchoRequest { Message = "y" }]);

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(reply.Message, Is.EqualTo("x+y"));
    }

    [Test]
    public async Task DuplexStreaming_ShouldExchangeInBothDirections()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc(grpc => grpc.AddClient("Echo", _address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc duplex", TestMethod());
        var client = context.Grpc("Echo");

        using var call = client.DuplexStreaming(ChatMethod);
        await call.RequestStream.WriteAsync(new EchoRequest { Message = "one" });
        await call.RequestStream.WriteAsync(new EchoRequest { Message = "two" });
        await call.RequestStream.CompleteAsync();
        var replies = new List<string>();
        await foreach (var reply in call.ResponseStream.ReadAllAsync())
        {
            replies.Add(reply.Message);
        }

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(replies, Is.EqualTo(new[] { "ONE", "TWO" }));
    }

    [Test]
    public async Task ApplicationTransport_ShouldBackTheChannel()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services =>
        {
            // The application's transport: a client under the application's name, exactly what
            // AddAspNetCoreServer registers, so the deferred channel resolves it at first call.
            services.AddSingleton(new ProtoApplicationTransport("Echo", "Echo"));
            services.AddSingleton<IProtoClientInitializer>(new TransportClientInitializer("Echo", _address));
        });
        builder.AddApplication("Echo", app => app.AddGrpc(grpc => grpc.AddClient("Default")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc transport", ApplicationTransportTestMethod());
        var client = context.Grpc();

        var reply = await client.UnaryAsync(SayMethod, new EchoRequest { Message = "transport" });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(reply.Message, Is.EqualTo("transport"));
            var entity = host.Trace.Snapshot().Tests.Single().Entities!.Single(candidate =>
                candidate.Kind == ProtoTraceEntityKinds.Client && candidate.Id.Contains("ProtoGrpcClient"));
            Assert.That(entity.State["client.endpoint_source"], Is.EqualTo("deferred"));
        });
    }

    private sealed class TransportClientInitializer(string name, string address) : IProtoClientInitializer<HttpClient>
    {
        public string Name { get; } = name;

        public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
        {
            var client = new HttpClient { BaseAddress = new Uri(address) };
            context.RegisterClient(client, Name);
            return Task.FromResult(true);
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static MethodInfo TestMethod()
        => typeof(GrpcIntegrationTests).GetMethod(nameof(Placeholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    [Auth<BearerTokenAuthenticator>("shared-token")]
    private static void AuthenticatedPlaceholder()
    {
    }

    private static MethodInfo AuthenticatedTestMethod()
        => typeof(GrpcIntegrationTests).GetMethod(nameof(AuthenticatedPlaceholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    [Application("Echo")]
    private static void ApplicationTransportPlaceholder()
    {
    }

    private static MethodInfo ApplicationTransportTestMethod()
        => typeof(GrpcIntegrationTests).GetMethod(nameof(ApplicationTransportPlaceholder), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void Placeholder()
    {
    }
}
