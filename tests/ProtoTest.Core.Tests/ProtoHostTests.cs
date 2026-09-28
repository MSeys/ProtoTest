namespace ProtoTest.Core.Tests;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

[TestFixture]
[NonParallelizable]
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
        await host.StartTestAsync("TestSequence", "00123", TestMethods.Placeholder, [testAttribute]);

        // The test body.
        executionLog.Add("TestBody");

        await host.CompleteTestAsync();

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
        var exception = Assert.Throws<InvalidOperationException>(() => _ = ProtoHost.CurrentContext);
        Assert.That(exception!.Message, Does.Contain("FindTraceWriter"));
    }

    [Test]
    public void CurrentHost_ShouldNameTheFix_WhenNoHostIsActive()
    {
        // This fixture is non-parallel, so no other test's host is active here.
        var exception = Assert.Throws<InvalidOperationException>(() => _ = ProtoHost.CurrentHost);
        Assert.That(exception!.Message, Does.Contain("ProtoTestAssembly"));
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
    public async Task MultipleHosts_ShouldResolveTheHostOwningTheCurrentContext()
    {
        await using var first = new ProtoHost(new ServiceCollection().BuildServiceProvider());
        await using var second = new ProtoHost(new ServiceCollection().BuildServiceProvider());

        Assert.Throws<InvalidOperationException>(() => _ = Proto.Host);

        await first.StartTestAsync("First", "00001", TestMethods.Placeholder);
        Assert.That(Proto.Host, Is.SameAs(first));
        await first.CompleteTestAsync();
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
        await host.StartTestAsync("TestDisposal", "00456", TestMethods.Placeholder);

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
