namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

[TestFixture]
public class ProtoRunResourceTests
{
    [Test]
    public async Task RunResource_ShouldOutliveTheRunAndBeReleasedWithTheHost()
    {
        // Arrange
        var released = new List<string>();
        var builder = new ProtoHostBuilder();
        builder.AddResource(new ProtoResource(
            "database:container",
            "database",
            "Postgres container",
            _ =>
            {
                released.Add("database:container");
                return ValueTask.CompletedTask;
            },
            ProtoResourceScope.Run));
        var host = builder.Build();

        // Act
        await host.StartAsync();
        await host.StopAsync();

        // Assert: reports may still need it, so the run stopping is not enough.
        Assert.That(released, Is.Empty);

        await host.DisposeAsync();
        Assert.That(released, Is.EqualTo(new[] { "database:container" }));
    }

    [Test]
    public async Task RunResource_ShouldSurfaceReleaseFailures()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddResource(new ProtoResource(
            "broken",
            "database",
            "Broken container",
            _ => throw new InvalidOperationException("The container refused to stop."),
            ProtoResourceScope.Run));
        var host = builder.Build();
        await host.StartAsync();

        // Act
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("The container refused to stop."));
    }

    [Test]
    public async Task RunResources_ShouldKeepReleasingAfterOneFails()
    {
        // Arrange
        var released = new List<string>();
        var builder = new ProtoHostBuilder();
        builder.AddResource(new ProtoResource(
            "broken", "database", "Broken container",
            _ => throw new InvalidOperationException("boom"), ProtoResourceScope.Run));
        builder.AddResource(new ProtoResource(
            "healthy", "database", "Healthy container",
            _ =>
            {
                released.Add("healthy");
                return ValueTask.CompletedTask;
            },
            ProtoResourceScope.Run));
        var host = builder.Build();
        await host.StartAsync();

        // Act: two failures would be needed for an aggregate, one is rethrown as itself.
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.DisposeAsync());

        // Assert
        Assert.That(exception!.Message, Is.EqualTo("boom"));
        Assert.That(released, Is.EqualTo(new[] { "healthy" }));
    }

    [Test]
    public void AddResource_ShouldRejectTestScopedResources()
    {
        // Arrange
        var builder = new ProtoHostBuilder();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => builder.AddResource(
            new ProtoResource("client", "client", "A test-scoped client", _ => default)));
        Assert.That(exception!.Message, Does.Contain("only run-scoped resources belong to the host"));
    }

    [Test]
    public void RegisterResource_ShouldRejectRunScopedResources()
    {
        // Arrange
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = new ProtoExecutionContext("Test", scope, "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => context.RegisterResource(
            new ProtoResource("container", "database", "Container", _ => default, ProtoResourceScope.Run)));
        Assert.That(exception!.Message, Does.Contain("AddResource"));
    }
}
