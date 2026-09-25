namespace ProtoTest.Core.Tests;

using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
public sealed class ReadinessTests
{
    [Test]
    public async Task AddReadinessProbe_ShouldRunAtItsRegistrationPositionAndRecordEvidence()
    {
        var order = new List<string>();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(new OrderingInfrastructure(order));
        builder.AddReadinessProbe("piece ready", _ =>
        {
            order.Add("probe");
            return ValueTask.FromResult(true);
        });
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Kind == "readiness");
        Assert.Multiple(() =>
        {
            Assert.That(order, Is.EqualTo(new[] { "piece", "probe" }), "the probe runs after the piece before it");
            Assert.That(entity.Id, Is.EqualTo("readiness:piece ready"));
            Assert.That(entity.State["readiness.attempts"], Is.EqualTo("1"));
            Assert.That(entity.State, Contains.Key("readiness.waitedMs"));
        });
    }

    [Test]
    public async Task AddReadinessProbe_WhenNeverReady_ShouldFailTheRunWithTheProbeAndLastError()
    {
        var attempts = 0;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureReadiness(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(200);
            options.Interval = TimeSpan.FromMilliseconds(20);
        });
        builder.AddReadinessProbe("service ready", _ =>
        {
            attempts++;
            throw new InvalidOperationException("connection refused");
        });
        await using var host = builder.Build();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("service ready"));
            Assert.That(exception.Message, Does.Contain("not satisfied"));
            Assert.That(exception.Message, Does.Contain("connection refused"));
            Assert.That(attempts, Is.GreaterThan(1), "the probe is retried until the timeout");
        });
    }

    [Test]
    public async Task AddReadinessProbe_ShouldRetryUntilTheCheckSucceeds()
    {
        var attempts = 0;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureReadiness(options => options.Interval = TimeSpan.FromMilliseconds(10));
        builder.AddReadinessProbe("eventually ready", _ =>
        {
            attempts++;
            return ValueTask.FromResult(attempts >= 3);
        });
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Kind == "readiness");
        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(entity.State["readiness.attempts"], Is.EqualTo("3"));
        });
    }

    [Test]
    public async Task TcpProbe_ShouldReportAnOpenListenerAndAClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var probe = ProtoReadiness.Tcp("127.0.0.1", port);

        var open = await probe(CancellationToken.None);
        listener.Stop();
        var closed = await probe(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(open, Is.True);
            Assert.That(closed, Is.False, "a refused connection is not ready");
        });
    }

    [Test]
    public async Task HttpReadiness_ShouldWaitForAPublishedAddressToAnswer()
    {
        var port = FreePort();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = $"http://127.0.0.1:{port}"
            }));
        builder.ConfigureReadiness(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(10);
            options.Interval = TimeSpan.FromMilliseconds(20);
        });
        builder.AddHttpReadiness("Api");
        await using var host = builder.Build();

        var start = host.StartAsync();
        await Task.Delay(150);
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var serve = ServeOnceAsync(listener);
        await start;
        await host.StopAsync();
        await serve;

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Kind == "readiness");
        Assert.Multiple(() =>
        {
            Assert.That(entity.State["readiness.url"], Does.Contain($"127.0.0.1:{port}"));
            Assert.That(int.Parse(entity.State["readiness.attempts"]!), Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task HttpReadiness_WithoutAnAddressOrAnInProcessServer_ShouldNameTheGap()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddHttpReadiness("Api");
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Kind == "readiness");
        var reason = entity.State["readiness.skipped"];
        Assert.Multiple(() =>
        {
            Assert.That(reason, Does.Contain("no in-process server"));
            Assert.That(reason, Does.Contain("ProtoTest:Applications:Api:BaseUrl"));
            Assert.That(reason, Does.Not.Contain("runs in-process"));
        });
    }

    [Test]
    public async Task HttpReadiness_WithASettingsPublishedAddress_ShouldWaitForIt()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(
            new PublishedAddressInfrastructure("Api", $"http://127.0.0.1:{port}"),
            "ProtoTest:Applications:Api:BaseUrl");
        builder.ConfigureReadiness(options => options.Interval = TimeSpan.FromMilliseconds(20));
        builder.AddHttpReadiness("Api");
        await using var host = builder.Build();

        // The listener is already accepting, so the probe's first HTTP attempt must succeed; no sleep.
        var start = host.StartAsync();
        var serve = ServeOnceAsync(listener);
        await start;
        await host.StopAsync();
        await serve;

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Kind == "readiness");
        Assert.Multiple(() =>
        {
            Assert.That(entity.State["readiness.url"], Does.Contain($"127.0.0.1:{port}"));
            Assert.That(entity.State, Does.Not.ContainKey("readiness.skipped"));
        });
    }

    [Test]
    public async Task HttpReadiness_RegisteredBeforeItsAddressPublisher_ShouldRecordTheOrderingReason()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddHttpReadiness("Api");
        builder.AddInfrastructure(
            new PublishedAddressInfrastructure("Api", "http://127.0.0.1:1"),
            "ProtoTest:Applications:Api:BaseUrl");
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Kind == "readiness");
        var reason = entity.State["readiness.skipped"];
        Assert.Multiple(() =>
        {
            Assert.That(reason, Does.Contain("registration position"));
            Assert.That(reason, Does.Contain("ProtoTest:Applications:Api:BaseUrl"));
            Assert.That(reason, Does.Not.Contain("runs in-process"), "a later publisher is not an in-process application");
        });
    }

    [Test]
    public async Task ConfigureReadiness_FromConfiguration_ShouldBindOverTheCodeValues()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Readiness:Timeout"] = "00:00:00.200",
                ["ProtoTest:Readiness:Interval"] = "00:00:00.020"
            }));
        builder.AddReadinessProbe("service ready", _ => ValueTask.FromResult(false));
        await using var host = builder.Build();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.That(
            exception!.Message,
            Does.Contain("0.2s").Or.Contain("0,2s"),
            "ProtoTest:Readiness bound over the default timeout");
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static async Task ServeOnceAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        // Read the request before answering: writing first and closing makes Windows abort the
        // connection while the client is still sending, and the probe rightly reports "not ready".
        var request = new byte[4096];
        await client.GetStream().ReadAsync(request);
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        await client.GetStream().WriteAsync(response);
        await client.GetStream().FlushAsync();
    }

    private sealed class OrderingInfrastructure(List<string> order) : IProtoInfrastructure
    {
        public string Id => "piece";

        public string Kind => "piece";

        public string Description => "Counts start order";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public ValueTask StartAsync(CancellationToken cancellationToken = default)
        {
            order.Add("piece");
            return ValueTask.CompletedTask;
        }

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }
}
