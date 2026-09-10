namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

[TestFixture]
public sealed class ProtoHostLifecycleTests
{
    [Test]
    public async Task StartTest_ShouldRejectSecondActiveContext()
    {
        await using var host = CreateHost();
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

        await host.StartTestAsync("First", "00001", method);

        Assert.Throws<InvalidOperationException>(() => host.StartTestAsync("Second", "00002", method));

        await host.CompleteTestAsync();
    }

    [Test]
    public async Task StartAsync_ShouldAllowRetryWhenBeforeRunHookFails()
    {
        var hook = new FailOnceRunHook();
        var services = new ServiceCollection();
        services.AddSingleton<IProtoRunHook>(hook);

        await using var host = new ProtoHost(services.BuildServiceProvider());

        Assert.ThrowsAsync<InvalidOperationException>(async () => await host.StartAsync());
        await host.StartAsync();
        await host.StopAsync();

        Assert.That(hook.BeforeRunAttempts, Is.EqualTo(2));
    }

    [Test]
    public async Task CompleteTest_ShouldReuseAttributesProvidedAtStart()
    {
        await using var host = CreateHost();
        var attribute = new StatefulAttribute();

        await host.StartTestAsync(
            "StatefulAttribute",
            "00001",
            (MethodInfo)MethodInfo.GetCurrentMethod()!,
            [attribute]);
        await host.CompleteTestAsync();

        Assert.That(attribute.AfterObservedBeforeState, Is.True);
    }

    [Test]
    public async Task ParallelTests_ShouldKeepAmbientContextsIsolated()
    {
        await using var host = CreateHost();
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;

        async Task RunTestAsync(string name, string id)
        {
            await host.StartTestAsync(name, id, method);
            try
            {
                if (Interlocked.Increment(ref startedCount) == 2)
                {
                    bothStarted.SetResult();
                }

                await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await Task.Yield();

                Assert.That(Proto.Context.TestName, Is.EqualTo(name));
                Assert.That(Proto.Context.TestId, Is.EqualTo(id));
                Assert.That(ProtoHost.CurrentHost, Is.SameAs(host));
            }
            finally
            {
                await host.CompleteTestAsync();
            }
        }

        await Task.WhenAll(
            Task.Run(() => RunTestAsync("First", "00001")),
            Task.Run(() => RunTestAsync("Second", "00002")));
    }

    private static ProtoHost CreateHost()
        => new(new ServiceCollection().BuildServiceProvider());

    private sealed class FailOnceRunHook : IProtoRunHook
    {
        public int BeforeRunAttempts { get; private set; }

        public Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            BeforeRunAttempts++;
            if (BeforeRunAttempts == 1)
            {
                throw new InvalidOperationException("Expected test failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StatefulAttribute : ProtoAttribute
    {
        private bool _beforeRan;

        public bool AfterObservedBeforeState { get; private set; }

        public override Task BeforeTestAsync(ProtoExecutionContext context)
        {
            _beforeRan = true;
            return Task.CompletedTask;
        }

        public override Task AfterTestAsync(ProtoExecutionContext context)
        {
            AfterObservedBeforeState = _beforeRan;
            return Task.CompletedTask;
        }
    }
}
