namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Characterization for <see cref="ProtoTestHostLifetime"/>. These tests pin its observable behavior,
/// including the check-then-act window, and observe per-host behavior instead of the process-wide
/// host registry, because the suite runs fixtures in parallel and the registry is shared.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ProtoTestHostLifetimeTests
{
    [Test]
    public async Task StartAsync_WhenAlreadyInitialized_ShouldThrowAndKeepTheFirstHost()
    {
        // Arrange
        var lifetime = new ProtoTestHostLifetime();
        await lifetime.StartAsync(ConfigureHost);
        var first = lifetime.Host;

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.StartAsync(ConfigureHost));

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("already"));
            Assert.That(lifetime.Host, Is.SameAs(first), "the first host stays the initialized one");
        });

        await lifetime.StopAsync();
        Assert.Throws<InvalidOperationException>(
            () => _ = lifetime.Host,
            "a stopped lifetime no longer exposes a host");
    }

    [Test]
    public async Task StartAsync_WhenTheStartFails_ShouldStayRetryable()
    {
        // Arrange
        var lifetime = new ProtoTestHostLifetime();
        var hook = new FailOnceRunHook();
        Action<IProtoHostBuilder> configure = builder =>
        {
            ConfigureHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        };

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() => lifetime.StartAsync(configure));
        await lifetime.StartAsync(configure);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Is.EqualTo("first attempt"));
            Assert.That(hook.BeforeRunCount, Is.EqualTo(2), "the retry ran the run hooks again");
            Assert.That(lifetime.Host, Is.Not.Null);
        });

        await lifetime.StopAsync();
    }

    [Test]
    public async Task StartAsync_ConcurrentCalls_ShouldStartOneHostAndRejectTheOther()
    {
        // The start is single-flight. A concurrent second call is rejected
        // while the first is in flight; the first's failure leaves the lifetime retryable.
        var lifetime = new ProtoTestHostLifetime();
        var firstHook = new ThrowingRunHook();
        var first = lifetime.StartAsync(builder =>
        {
            ConfigureHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(firstHook));
        });
        var second = lifetime.StartAsync(ConfigureHost);

        // Act & Assert: the second call loses before anything is built.
        var rejection = Assert.ThrowsAsync<InvalidOperationException>(async () => await second);
        Assert.That(rejection!.Message, Does.Contain("already been initialized"));
        Assert.That(firstHook.EnterCount, Is.EqualTo(1), "the first attempt reached its run hook");

        firstHook.Release();
        Assert.CatchAsync(async () => await first);
        Assert.Throws<InvalidOperationException>(() => _ = lifetime.Host, "the failed start initialized nothing");

        // The lifetime stays retryable.
        await lifetime.StartAsync(ConfigureHost);
        Assert.That(lifetime.Host, Is.Not.Null);
        await lifetime.StopAsync();
    }

    [Test]
    public async Task StopAsync_DuringStart_ShouldWaitThenStopAndRunAfterRunOnce()
    {
        // A stop that arrives while BeforeRun is still awaiting waits for that start, then
        // stops the host. AfterRun runs once and the lifetime exposes no host.
        var lifetime = new ProtoTestHostLifetime();
        var hook = new BlockingRunHook();
        var starting = lifetime.StartAsync(builder =>
        {
            ConfigureHost(builder);
            builder.ConfigureServices(services => services.AddSingleton<IProtoRunHook>(hook));
        });

        await hook.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopping = lifetime.StopAsync();
        Assert.That(stopping.IsCompleted, Is.False, "Stop returned while the start was still blocked");

        hook.Release();
        await starting;
        await stopping;

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(
                () => _ = lifetime.Host,
                "a stop that waited out the start does not leave a host published");
            Assert.That(hook.AfterRunCount, Is.EqualTo(1));
        });
    }

    private static void ConfigureHost(IProtoHostBuilder builder)
        => builder.ConfigureTracing(options => options.Enabled = false);

    private sealed class FailOnceRunHook : IProtoRunHook
    {
        public int BeforeRunCount { get; private set; }

        public Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            BeforeRunCount++;
            return BeforeRunCount == 1
                ? Task.FromException(new InvalidOperationException("first attempt"))
                : Task.CompletedTask;
        }

        public Task AfterRunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingRunHook : IProtoRunHook
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;

        public int EnterCount => Volatile.Read(ref _entered);

        public void Release() => _release.TrySetResult();

        public async Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _entered);
            await _release.Task.WaitAsync(cancellationToken);
            throw new InvalidOperationException("The first start failed inside its run hook.");
        }

        public Task AfterRunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BlockingRunHook : IProtoRunHook
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _afterRunCount;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int AfterRunCount => Volatile.Read(ref _afterRunCount);

        public void Release() => _release.TrySetResult();

        public async Task BeforeRunAsync(CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public Task AfterRunAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _afterRunCount);
            return Task.CompletedTask;
        }
    }
}
