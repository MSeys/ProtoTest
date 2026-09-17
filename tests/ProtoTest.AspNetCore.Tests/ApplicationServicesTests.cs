namespace ProtoTest.AspNetCore.Tests;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using System.Reflection;

[TestFixture]
public sealed class ApplicationServicesTests
{
    [Test]
    public async Task ApplicationServices_ShouldOwnAPerTestScopeAndResolveScopedServices()
    {
        // Arrange
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();
        await host.StartTestAsync("Scoped_Domain_Access", "00010", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // Act
            var probe = Proto.Context.ApplicationServices<SampleApi.Program>("Default")
                .GetRequiredService<SampleApi.IScopedProbe>();
            var again = Proto.Context.ApplicationServices<SampleApi.Program>("Default")
                .GetRequiredService<SampleApi.IScopedProbe>();

            // Assert: one scope per test, owned as a resource.
            Assert.Multiple(() =>
            {
                Assert.That(again, Is.SameAs(probe));
                Assert.That(probe.Disposed, Is.False);
                Assert.That(Proto.Context.Resources.Single(resource => resource.Kind == "application").State,
                    Is.EqualTo(ProtoResourceState.Registered));
            });
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task ServerService_ShouldResolveScopedServices()
    {
        // Arrange
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();
        await host.StartTestAsync("Scoped_Server_Service", "00011", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // Act: resolving from the application's root container would throw for a scoped service.
            var probe = Proto.Context.ServerService<SampleApi.Program, SampleApi.IScopedProbe>("Default");

            // Assert
            Assert.That(probe.Instance, Is.GreaterThan(0));
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task ApplicationServices_ShouldDisposeTheScopeWhenTheTestEnds()
    {
        // Arrange
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();
        await host.StartTestAsync("Scoped_Disposal", "00012", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        var probe = Proto.Context.ApplicationServices<SampleApi.Program>("Default")
            .GetRequiredService<SampleApi.IScopedProbe>();

        // Act
        await host.CompleteTestAsync();

        // Assert
        Assert.That(probe.Disposed, Is.True);
        await host.DisposeAsync();
    }
}
