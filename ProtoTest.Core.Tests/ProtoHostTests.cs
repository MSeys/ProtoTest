namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

[TestFixture]
public class ProtoHostTests
{
    [Test]
    public async Task Pipeline_ShouldExecuteHooksAndAttributesInCorrectSequence()
    {
        // Arrange
        var executionLog = new List<string>();
        var globalHook = new TrackingHook(executionLog);
        var testAttribute = new TrackingAttribute(executionLog);

        var services = new ServiceCollection().BuildServiceProvider();
        await using var host = new ProtoHost(services, [globalHook]);

        // Act
        // 1. Synchronously bind context to caller frame
        host.BeginTestContext("TestSequence", "id-123");

        // 2. Asynchronously execute pre-test hooks & attributes
        await host.ExecuteBeforeHooksAsync([testAttribute]);

        // 3. Test execution body
        executionLog.Add("TestBody");

        // 4. Asynchronously execute post-test hooks & dispose scope
        await host.ExecuteAfterHooksAsync([testAttribute]);

        // 5. Synchronously unbind context from caller frame
        host.EndTestContext();

        // Assert
        var expectedSequence = new[]
        {
            "GlobalHook:Before",
            "Attribute:Before",
            "TestBody",
            "Attribute:After",
            "GlobalHook:After"
        };

        Assert.That(executionLog, Is.EqualTo(expectedSequence));
    }

    [Test]
    public void Current_ShouldThrowInvalidOperationException_WhenAccessedOutsideOfTestScope()
    {
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => _ = ProtoHost.Current);
    }

    [Test]
    public async Task EndTestContext_ShouldDisposeScopeAndClearCurrentContext()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped<DisposableDependency>();
        var rootProvider = serviceCollection.BuildServiceProvider();

        await using var host = new ProtoHost(rootProvider, []);

        // Act
        host.BeginTestContext("TestDisposal", "id-456");

        // Resolve dependency while context is active on the current thread
        var dependency = ProtoHost.Current.Services.GetRequiredService<DisposableDependency>();

        await host.ExecuteAfterHooksAsync();
        host.EndTestContext();

        // Assert
        Assert.That(dependency.IsDisposed, Is.True);
        Assert.Throws<InvalidOperationException>(() => _ = ProtoHost.Current);
    }

    private sealed class TrackingHook(List<string> log) : IProtoHook
    {
        public Task BeforeTestAsync(ProtoExecutionContext context)
        {
            log.Add("GlobalHook:Before");
            return Task.CompletedTask;
        }

        public Task AfterTestAsync(ProtoExecutionContext context)
        {
            log.Add("GlobalHook:After");
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingAttribute(List<string> log) : ProtoAttribute
    {
        public override Task BeforeTestAsync(ProtoExecutionContext context)
        {
            log.Add("Attribute:Before");
            return Task.CompletedTask;
        }

        public override Task AfterTestAsync(ProtoExecutionContext context)
        {
            log.Add("Attribute:After");
            return Task.CompletedTask;
        }
    }

    private sealed class DisposableDependency : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
    }
}