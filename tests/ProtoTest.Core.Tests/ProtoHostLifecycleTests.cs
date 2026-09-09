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
