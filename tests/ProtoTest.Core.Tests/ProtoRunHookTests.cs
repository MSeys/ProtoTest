namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

[TestFixture]
public class ProtoRunHookTests
{
    [Test]
    public async Task StartAndStop_ShouldOrderRunHooksAndPropagateCancellation()
    {
        // Arrange
        var events = new List<string>();
        using var cancellationSource = new CancellationTokenSource();
        var first = new TrackingRunHook("First", 10, events, cancellationSource.Token);
        var second = new TrackingRunHook("Second", 20, events, cancellationSource.Token);
        var services = new ServiceCollection();
        services.AddSingleton<IProtoRunHook>(first);
        services.AddSingleton<IProtoRunHook>(second);
        await using var host = new ProtoHost(services.BuildServiceProvider());

        // Act
        await host.StartAsync(cancellationSource.Token);
        await host.StopAsync(cancellationSource.Token);

        // Assert
        Assert.That(events, Is.EqualTo(new[]
        {
            "First:Before",
            "Second:Before",
            "Second:After",
            "First:After"
        }));
        Assert.That(first.ReceivedToken, Is.EqualTo(cancellationSource.Token));
        Assert.That(second.ReceivedToken, Is.EqualTo(cancellationSource.Token));
    }

    [Test]
    public async Task CompleteTest_ShouldDisposeContextAndClearAmbientState_WhenAfterHookFails()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<DisposableDependency>();
        services.AddSingleton<IProtoTestHook, ThrowingAfterHook>();
        await using var host = new ProtoHost(services.BuildServiceProvider());
        await host.StartTestAsync("FailureTest", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);
        var dependency = ProtoHost.CurrentContext.Service<DisposableDependency>();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () => await host.CompleteTestAsync());
        Assert.That(dependency.IsDisposed, Is.True);
        Assert.Throws<InvalidOperationException>(() => _ = ProtoHost.CurrentContext);
    }

    private sealed class TrackingRunHook(
        string name,
        int order,
        List<string> events,
        CancellationToken expectedToken) : IProtoRunHook
    {
        public int Order => order;
        public CancellationToken? ReceivedToken { get; private set; }

        public Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            Assert.That(cancellationToken, Is.EqualTo(expectedToken));
            ReceivedToken = cancellationToken;
            events.Add($"{name}:Before");
            return Task.CompletedTask;
        }

        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            Assert.That(cancellationToken, Is.EqualTo(expectedToken));
            ReceivedToken = cancellationToken;
            events.Add($"{name}:After");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingAfterHook : IProtoTestHook
    {
        public Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

        public Task AfterTestAsync(ProtoExecutionContext context)
            => Task.FromException(new InvalidOperationException("after hook failed"));
    }

    private sealed class DisposableDependency : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}
