namespace ProtoTest.Grpc.Tests;

using global::Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.AspNetCore;
using ProtoTest.Core;
using ProtoTest.Grpc.Tests.Echo;

/// <summary>
/// Pins the disclosed in-process deadline limit: a socket endpoint reports <c>DeadlineExceeded</c>
/// when a call overruns its deadline, while the in-process transport surfaces the server's abort
/// status (<c>Unknown</c> or <c>Internal</c>) when the deadline passes mid-call. The gRPC page's
/// Limits section states it; a suite asserting <c>DeadlineExceeded</c> must run against a socket.
/// </summary>
[TestFixture]
public sealed class InProcessDeadlineTests
{
    [Test]
    public async Task InProcessCall_WhenTheDeadlinePassesMidCall_ShouldSurfaceTheTransportAbort()
    {
        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.WebHost.UseTestServer();
        appBuilder.Services.AddGrpc();
        var app = appBuilder.Build();
        app.MapGrpcService<SlowEchoService>();
        await app.StartAsync();

        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(new ProtoApplicationTransport("Echo", "Echo"));
            services.AddSingleton<IProtoClientInitializer>(new FixedClientInitializer("Echo", app.GetTestClient()));
        });
        builder.AddApplication("Echo", application => application.AddGrpc(grpc => grpc.AddClient("Default")));
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync(
            "in-process deadline",
            TestMethods.Placeholder,
            [new ApplicationAttribute("Echo")]);
        var client = context.Grpc();

        var exception = Assert.CatchAsync(async () =>
            await client.UnaryAsync(
                EchoMethods.Say,
                new EchoRequest { Message = "deadline" },
                deadline: DateTime.UtcNow.AddMilliseconds(300)));

        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();
        await app.DisposeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.TypeOf<RpcException>());
            Assert.That(
                ((RpcException)exception!).StatusCode,
                Is.Not.EqualTo(StatusCode.DeadlineExceeded),
                "the in-process transport reports the server's abort, not the socket's deadline status");
        });
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
}
