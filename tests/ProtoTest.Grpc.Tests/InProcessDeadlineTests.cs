namespace ProtoTest.Grpc.Tests;

using global::Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Grpc.Tests.Echo;

/// <summary>
/// A call that overruns its deadline reports <c>DeadlineExceeded</c> in-process as over a socket. In-process
/// the server's deadline enforcement aborts the request and can beat the client's own timer; the client
/// reports that abort as the deadline it is.
/// </summary>
[TestFixture]
public sealed class InProcessDeadlineTests
{
    [Test]
    public async Task InProcessCall_WhenTheDeadlinePassesMidCall_ShouldReportDeadlineExceeded()
    {
        await using var app = await StartInProcessAsync<SlowEchoService>();
        await using var host = await StartHostAsync(app);
        var client = (await host.StartTestAsync("in-process deadline", TestMethods.Placeholder, [new ApplicationAttribute("Echo")])).Grpc();

        var exception = Assert.CatchAsync(async () =>
            await client.UnaryAsync(
                EchoMethods.Say,
                new EchoRequest { Message = "deadline" },
                deadline: DateTime.UtcNow.AddMilliseconds(300)));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<RpcException>());
            Assert.That(((RpcException)exception!).StatusCode, Is.EqualTo(StatusCode.DeadlineExceeded));
        });
    }

    [Test]
    public async Task InProcessCall_WhenTheServerFailsBeforeTheDeadline_ShouldKeepTheServersStatus()
    {
        await using var app = await StartInProcessAsync<FailingEchoService>();
        await using var host = await StartHostAsync(app);
        var client = (await host.StartTestAsync("in-process deadline", TestMethods.Placeholder, [new ApplicationAttribute("Echo")])).Grpc();

        var exception = Assert.CatchAsync(async () =>
            await client.UnaryAsync(
                EchoMethods.Say,
                new EchoRequest { Message = "fails" },
                deadline: DateTime.UtcNow.AddSeconds(30)));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(((RpcException)exception!).StatusCode, Is.EqualTo(StatusCode.Internal));
    }

    [Test]
    public async Task SocketCall_WhenTheDeadlinePasses_ShouldReportDeadlineExceeded()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddGrpc(grpc => grpc.AddClient("Echo", GrpcTestServer.Address));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("socket deadline", TestMethods.Placeholder);
        var client = context.Grpc("Echo");

        var exception = Assert.CatchAsync(async () =>
            await client.UnaryAsync(
                EchoMethods.Say,
                new EchoRequest { Message = "deadline" },
                deadline: DateTime.UtcNow.AddTicks(1)));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<RpcException>());
            Assert.That(((RpcException)exception!).StatusCode, Is.EqualTo(StatusCode.DeadlineExceeded));
        });
    }

    private static async Task<WebApplication> StartInProcessAsync<TService>()
        where TService : class
    {
        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.WebHost.UseTestServer();
        appBuilder.Services.AddGrpc();
        var app = appBuilder.Build();
        app.MapGrpcService<TService>();
        await app.StartAsync();
        return app;
    }

    private static async Task<ProtoHost> StartHostAsync(WebApplication app)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(new ProtoApplicationTransport("Echo", "Echo"));
            services.AddSingleton<IProtoClientInitializer>(new FixedClientInitializer("Echo", app.GetTestClient()));
        });
        builder.AddApplication("Echo", application => application.AddGrpc(grpc => grpc.AddClient("Default")));
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private sealed class FixedClientInitializer(string name, HttpClient client) : IProtoClientInitializer<HttpClient>
    {
        public string Name { get; } = name;

        public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
        {
            context.RegisterClient(client, Name);
            return Task.FromResult(true);
        }
    }

    /// <summary>The echo service with a Say that overruns any short deadline.</summary>
    private sealed class SlowEchoService : ProtoTest.Grpc.Tests.Echo.Echo.EchoBase
    {
        public override async Task<EchoReply> Say(EchoRequest request, ServerCallContext context)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken);
            return new EchoReply { Message = request.Message };
        }
    }

    /// <summary>The echo service with a Say that fails at once.</summary>
    private sealed class FailingEchoService : ProtoTest.Grpc.Tests.Echo.Echo.EchoBase
    {
        public override Task<EchoReply> Say(EchoRequest request, ServerCallContext context)
            => throw new RpcException(new Status(StatusCode.Internal, "the echo broke"));
    }
}
