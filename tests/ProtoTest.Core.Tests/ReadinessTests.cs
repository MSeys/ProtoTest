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
        builder.AddInfrastructure(
            "ordering",
            chain => chain.Use(new ProtoTargetProvider("ordering", new OrderingInfrastructure(order))));
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
        await using var listener = SingleConnectionListener.Start();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = listener.Address
            }));
        builder.ConfigureReadiness(options =>
        {
            options.Timeout = TimeSpan.FromSeconds(10);
            options.Interval = TimeSpan.FromMilliseconds(20);
        });
        builder.AddHttpReadiness("Api");
        await using var host = builder.Build();

        var start = host.StartAsync();
        await listener.RequestReceived.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(start.IsCompleted, Is.False, "readiness waits while the address has not answered");
        listener.Answer();
        await start;
        await host.StopAsync();
        await listener.Completed;

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Kind == "readiness");
        Assert.Multiple(() =>
        {
            Assert.That(entity.State["readiness.url"], Does.Contain($"127.0.0.1:{listener.Port}"));
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
        await using var listener = SingleConnectionListener.Start();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddInfrastructure(
            "Api address",
            chain => chain
                .UseConfigured()
                .Use(new ProtoTargetProvider("published", new PublishedAddressInfrastructure("Api", listener.Address))),
            "ProtoTest:Applications:Api:BaseUrl");
        builder.ConfigureReadiness(options => options.Interval = TimeSpan.FromMilliseconds(20));
        builder.AddHttpReadiness("Api");
        await using var host = builder.Build();

        // The probe's first request must succeed once it is answered; the signal proves it arrived.
        var start = host.StartAsync();
        await listener.RequestReceived.WaitAsync(TimeSpan.FromSeconds(10));
        listener.Answer();
        await start;
        await host.StopAsync();
        await listener.Completed;

        var entity = host.Trace.Snapshot().Entities!.Single(candidate => candidate.Kind == "readiness");
        Assert.Multiple(() =>
        {
            Assert.That(entity.State["readiness.url"], Does.Contain($"127.0.0.1:{listener.Port}"));
            Assert.That(entity.State["readiness.attempts"], Is.EqualTo("1"), "the first request answers");
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
            "Api address",
            chain => chain
                .UseConfigured()
                .Use(new ProtoTargetProvider("published", new PublishedAddressInfrastructure("Api", "http://127.0.0.1:1"))),
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

    [TestCase("not-a-url")]
    [TestCase("ftp://127.0.0.1/health")]
    public async Task HttpReadiness_WhenTheAddressIsNotAnAbsoluteHttpUrl_ShouldFailNamingTheKey(string address)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = address
            }));
        // Short, so a regression that probes the bad address instead of validating it fails fast.
        builder.ConfigureReadiness(options => options.Timeout = TimeSpan.FromMilliseconds(200));
        builder.AddHttpReadiness("Api");
        await using var host = builder.Build();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.That(
            exception!.Message,
            Does.Contain("ProtoTest:Applications:Api:BaseUrl"),
            "the malformed address is rejected at run start, naming the key to fix");
    }

    [Test]
    public async Task HttpReadiness_WhenNothingAnswers_ShouldFailNamingTheUrlAndTheLastError()
    {
        var port = TestNetworking.FreePort();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = $"http://127.0.0.1:{port}"
            }));
        builder.ConfigureReadiness(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(400);
            options.Interval = TimeSpan.FromMilliseconds(50);
        });
        builder.AddHttpReadiness("Api");
        await using var host = builder.Build();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain($"http://127.0.0.1:{port}/"),
                "the failure names the URL that was probed");
            Assert.That(exception.Message, Does.Contain("Last error:"), "the failure carries the last error");
            Assert.That(exception.Message, Does.Contain("HttpRequestException"),
                "a refused connection is the error the trial needed to see");
        });
    }

    [Test]
    public async Task HttpReadiness_WhenEveryAnswerIsRejected_ShouldSayTheEndpointAnswered()
    {
        using var listener = new AnsweringListener();
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = listener.Address
            }));
        builder.ConfigureReadiness(options =>
        {
            options.Timeout = TimeSpan.FromMilliseconds(300);
            options.Interval = TimeSpan.FromMilliseconds(50);
        });
        builder.AddHttpReadiness("Api", ready: _ => false);
        await using var host = builder.Build();

        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());

        Assert.That(
            exception!.Message,
            Does.Contain("rejected every response"),
            "an answered-but-rejected probe names that branch instead of claiming an unreachable endpoint");
    }

    [Test]
    public void Build_WhenTheReadinessOptionsAreInvalid_ShouldFailConfigurationNotTheFirstProbe()
    {
        // The options register through ProtoOptionsRegistration, and the host forces that
        // resolve at Build, so validation runs once and a bad ProtoTest:Readiness is configuration error.
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Readiness:Timeout"] = "00:00:00"
            }));

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build());
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

    /// <summary>
    /// A loopback HTTP listener that answers every request with <c>200 OK</c>, so a readiness check
    /// that rejects responses is exercised past its first attempt against one address.
    /// </summary>
    private sealed class AnsweringListener : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serve;

        public AnsweringListener()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serve = ServeAsync();
        }

        public int Port { get; }

        public string Address => $"http://127.0.0.1:{Port}";

        public void Dispose()
        {
            _listener.Stop();
            try
            {
                _serve.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // The accept loop ends with the listener; teardown has nothing to report.
            }
        }

        private async Task ServeAsync()
        {
            var response = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (SocketException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                using (client)
                {
                    try
                    {
                        var request = new byte[4096];
                        await client.GetStream().ReadAsync(request);
                        await client.GetStream().WriteAsync(response);
                        await client.GetStream().FlushAsync();
                    }
                    catch (Exception exception) when (exception is IOException or SocketException)
                    {
                        return;
                    }
                }
            }
        }
    }
}
