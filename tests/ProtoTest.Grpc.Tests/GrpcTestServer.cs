namespace ProtoTest.Grpc.Tests;

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Grpc.Tests.Echo;

/// <summary>
/// Starts the echo server once for the whole namespace. A <see cref="SetUpFixture"/> is not affected
/// by <c>FixtureLifeCycle(InstancePerTestCase)</c>, so the server outlives every test instance and the
/// suite can run in parallel.
/// </summary>
[SetUpFixture]
public sealed class GrpcTestServer
{
    private static WebApplication _server = null!;

    /// <summary>The address the echo server listens on.</summary>
    public static string Address { get; private set; } = string.Empty;

    [OneTimeSetUp]
    public async Task StartAsync()
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
        Address = $"http://127.0.0.1:{port}";
    }

    [OneTimeTearDown]
    public async Task StopAsync() => await _server.DisposeAsync();

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
