namespace ProtoTest.Grpc.Tests;

using System.Collections.Concurrent;
using System.Reflection;
using global::Grpc.Core;
using global::Grpc.Net.Client;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Grpc.Tests.Echo;

[TestFixture]
public sealed class ProtoGrpcClientChannelTests
{
    [Test]
    public async Task GetInvokerAsync_WhenCallsRace_ShouldInstallOneChannelAndDisposeTheLoser()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc channel race", TestMethods.Placeholder);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var created = new ConcurrentQueue<GrpcChannel>();
        var started = 0;
        var client = new ProtoGrpcClient(context, "Race", new GrpcClientOptions(), async (_, _) =>
        {
            if (Interlocked.Increment(ref started) == 2) gate.TrySetResult();
            await gate.Task;
            var channel = GrpcChannel.ForAddress("http://127.0.0.1:1");
            created.Enqueue(channel);
            return channel;
        });

        var invokers = await Task.WhenAll(
            client.GetInvokerAsync(CancellationToken.None).AsTask(),
            client.GetInvokerAsync(CancellationToken.None).AsTask());

        Assert.Multiple(() =>
        {
            Assert.That(invokers[0], Is.SameAs(invokers[1]), "both calls share the installed invoker");
            Assert.That(created, Has.Count.EqualTo(2), "both calls created a channel before the gate opened");
            Assert.That(created.Count(CanCreateInvoker), Is.EqualTo(1),
                "the channel that lost the race was disposed under the gate instead of leaking");
        });

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task GetInvokerAsync_WhenDisposedDuringChannelCreation_ShouldDisposeTheLateChannel()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc disposed during create", TestMethods.Placeholder);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GrpcChannel? channel = null;
        var client = new ProtoGrpcClient(context, "DisposedWhileCreating", new GrpcClientOptions(), async (_, _) =>
        {
            await gate.Task;
            channel = GrpcChannel.ForAddress("http://127.0.0.1:1");
            return channel;
        });

        var invokerTask = client.GetInvokerAsync(CancellationToken.None).AsTask();
        client.Dispose();
        gate.TrySetResult();
        var exception = Assert.ThrowsAsync<ObjectDisposedException>(async () => await invokerTask);

        Assert.Multiple(() =>
        {
            Assert.That(exception, Is.Not.Null);
            Assert.That(channel, Is.Not.Null, "the factory had already created the channel");
            Assert.That(CanCreateInvoker(channel!), Is.False,
                "a channel created while disposal was in flight is disposed, not leaked");
        });

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
    }

    [Test]
    public async Task GetInvokerAsync_AfterDispose_ShouldNotInstallAChannel()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc disposed guard", TestMethods.Placeholder);

        var created = new ConcurrentQueue<GrpcChannel>();
        var client = new ProtoGrpcClient(context, "Disposed", new GrpcClientOptions(), (_, _) =>
        {
            var channel = GrpcChannel.ForAddress("http://127.0.0.1:1");
            created.Enqueue(channel);
            return ValueTask.FromResult(channel);
        });

        client.Dispose();

        Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await client.GetInvokerAsync(CancellationToken.None));
        Assert.That(created, Is.Empty, "a disposed client never creates a channel");

        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    [Test]
    public async Task UnaryAsync_WhenTheCallTokenIsCancelled_ShouldCancelTheInFlightCall()
    {
        var builder = new ProtoHostBuilder();
        builder.AddGrpc();
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("grpc call cancellation", TestMethods.Placeholder);

        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new ProtoGrpcClient(context, "Cancel", new GrpcClientOptions(), (_, _) =>
            ValueTask.FromResult(GrpcChannel.ForAddress(
                "http://127.0.0.1:1", new GrpcChannelOptions { HttpHandler = new BlockingHandler(requestStarted) })));

        using var cancellation = new CancellationTokenSource();
        var call = client.UnaryAsync(
            EchoMethods.Say,
            new EchoRequest { Message = "slow" },
            cancellationToken: cancellation.Token);

        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();

        var exception = Assert.CatchAsync(async () => await call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.That(
            exception,
            Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<RpcException>(),
            "the call must observe the cancellation token it was given");

        await host.CompleteTestAsync(ProtoTestResult.Failed(exception!));
    }

    /// <summary>Accepts one request, reports that it arrived, then waits for the call to be cancelled.</summary>
    private sealed class BlockingHandler(TaskCompletionSource requestStarted) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage();
        }
    }

    private static bool CanCreateInvoker(GrpcChannel channel)
    {
        try
        {
            _ = channel.CreateCallInvoker();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }


}
