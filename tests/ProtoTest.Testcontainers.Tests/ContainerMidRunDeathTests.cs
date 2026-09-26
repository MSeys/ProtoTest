namespace ProtoTest.Testcontainers.Tests;

using System.Net;
using System.Net.Sockets;
using System.Text;
using ProtoTest.Core;
using ProtoTest.Testcontainers;

/// <summary>
/// A container that dies mid-run. Nothing in
/// the framework watches a started container; the next use of its address must surface the transport's
/// own error in the affected test, not hang, and not pass.
/// </summary>
/// <remarks>
/// The suite runs without a container runtime, so the container is a deterministic stand-in: a stub
/// container whose process is a real loopback listener. <see cref="LoopbackContainer.Die"/> stops the
/// listener - exactly what a killed container process leaves behind, an address that no longer
/// answers - while the run-owned <see cref="ProtoContainerResource{TContainer}"/> keeps its started
/// state and address, because the framework does not poll a running container.
/// </remarks>
[TestFixture]
public sealed class ContainerMidRunDeathTests
{
    [Test]
    public async Task ContainerDeathMidRun_ShouldSurfaceTheOriginalTransportErrorInTheAffectedTest()
    {
        using var trace = new TemporaryTrace("container-death");
        var container = new LoopbackContainer();
        var resource = new LoopbackContainerResource(() => container);
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = trace.Path);
        builder.AddInfrastructure(resource, "ConnectionStrings:OrderDb");
        await using var host = builder.Build();
        await host.StartAsync();

        // The container is alive: the first test reaches it through the address the run published.
        await host.StartTestAsync("container alive", "00001", TestMethods.Placeholder);
        var address = Proto.Context.TryService<ProtoInfrastructureSettings>()!
            .Values["ConnectionStrings:OrderDb"];
        var answer = await QueryAsync(address!);
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        // Mid-run death: the endpoint goes away, the started resource does not notice - there is no
        // liveness monitor, so a dead container is only visible through the client that uses it.
        container.Die();
        Assert.Multiple(() =>
        {
            Assert.That(answer, Is.EqualTo("PONG"));
            Assert.That(resource.IsStarted, Is.True,
                "the framework does not notice the death by itself; the client does");
            Assert.That(resource.ConnectionString, Is.EqualTo(address),
                "the published address stays until the run releases the container (documented limit)");
        });

        // The affected test gets the transport's own error: a connection refusal from the dead
        // endpoint, not a swallowed failure, a ProtoTest timeout, or a pass.
        await host.StartTestAsync("container dead", "00002", TestMethods.Placeholder);
        var error = Assert.ThrowsAsync<SocketException>(async () => await QueryAsync(address!));
        await host.CompleteTestAsync(ProtoTestResult.Failed(error!));

        // The death is scoped to the test that used the address: a test that never touches the
        // container still runs normally, and the run still tears down.
        await host.StartTestAsync("no container use", "00003", TestMethods.Placeholder);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        var tests = host.Trace.Snapshot().Tests;
        Assert.Multiple(() =>
        {
            Assert.That(error!.SocketErrorCode, Is.EqualTo(SocketError.ConnectionRefused),
                "the affected test saw the real socket error");
            Assert.That(resource.IsStarted, Is.False, "run teardown released the dead container");
            Assert.That(container.DisposeCount, Is.EqualTo(1), "the release disposed it exactly once");
            Assert.That(tests.Single(test => test.TestId == "00001").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Succeeded));
            Assert.That(tests.Single(test => test.TestId == "00002").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Failed));
            Assert.That(tests.Single(test => test.TestId == "00002").Error!.Message,
                Is.EqualTo(error.Message),
                "the trace carries the original transport error, not a ProtoTest rewrite");
            Assert.That(tests.Single(test => test.TestId == "00003").Outcome,
                Is.EqualTo(ProtoTraceOutcome.Succeeded),
                "a test that does not use the container is unaffected");
        });
    }

    /// <summary>The client call the suite makes against the container's address: one request, one answer.</summary>
    private static async Task<string> QueryAsync(string connectionString)
    {
        var address = new Uri(connectionString);
        using var client = new TcpClient();
        await client.ConnectAsync(address.Host, address.Port);
        var buffer = new byte[4];
        var read = await client.GetStream().ReadAsync(buffer);
        return Encoding.ASCII.GetString(buffer, 0, read);
    }

    private sealed class LoopbackContainerResource(Func<LoopbackContainer> build)
        : ProtoContainerResource<LoopbackContainer>(
            build,
            (container, cancellationToken) => container.StartAsync(cancellationToken),
            container => container.ConnectionString)
    {
        public override string Id => "container:loopback";

        public override string Kind => "container";

        public override string Description => "Loopback container";
    }

    /// <summary>The stub container process: a loopback listener that can be killed deterministically.</summary>
    private sealed class LoopbackContainer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private Task? _serve;

        public string ConnectionString { get; private set; } = string.Empty;

        public int DisposeCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _listener.Start();
            ConnectionString = $"tcp://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _serve = ServeAsync();
            return Task.CompletedTask;
        }

        /// <summary>Kills the container process: the endpoint stops answering immediately.</summary>
        public void Die() => _listener.Stop();

        private async Task ServeAsync()
        {
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
                    await client.GetStream().WriteAsync("PONG"u8.ToArray());
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            DisposeCount++;
            Die();
            if (_serve is not null)
            {
                await _serve;
            }
        }
    }
}
