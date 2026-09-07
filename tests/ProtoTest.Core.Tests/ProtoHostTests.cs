namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Reflection;

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

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IProtoTestHook>(globalHook);
        await using var host = new ProtoHost(serviceCollection.BuildServiceProvider());

        // Act
        // 1. Create the test execution scope
        await host.StartTestAsync("TestSequence", "id-123", (MethodInfo)MethodInfo.GetCurrentMethod()!, [testAttribute]);

        // 3. Test execution body
        executionLog.Add("TestBody");

        // 4. Complete the test lifecycle and dispose its scope
        await host.CompleteTestAsync([testAttribute]);

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
        Assert.Throws<InvalidOperationException>(() => _ = ProtoHost.CurrentContext);
    }

    [Test]
    public async Task Builder_ShouldExposeTheSameHostToDiServices()
    {
        // Arrange
        var builder = new ProtoHostBuilder();

        // Act
        await using var host = builder.Build();

        // Assert
        Assert.That(ProtoHost.CurrentHost, Is.SameAs(host));
        Assert.That(Proto.Host, Is.SameAs(host));
        Assert.Throws<InvalidOperationException>(() => _ = Proto.Context);
    }

    [Test]
    public async Task Builder_ShouldRejectSecondBuild()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        await using var host = builder.Build();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Test]
    public async Task CompleteTest_ShouldDisposeScopeAndClearCurrentContext()
    {
        // Arrange
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped<DisposableDependency>();
        var rootProvider = serviceCollection.BuildServiceProvider();

        await using var host = new ProtoHost(rootProvider);

        // Act
        await host.StartTestAsync("TestDisposal", "id-456", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        // Resolve dependency while context is active on the current thread
        var dependency = ProtoHost.CurrentContext.Services.GetRequiredService<DisposableDependency>();

        await host.CompleteTestAsync();

        // Assert
        Assert.That(dependency.IsDisposed, Is.True);
        Assert.Throws<InvalidOperationException>(() => _ = ProtoHost.CurrentContext);
    }

    private sealed class TrackingHook(List<string> log) : IProtoTestHook
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