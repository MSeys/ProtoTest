namespace ProtoTest.AspNetCore.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Pins the per-instance server capability (audit REG-1/REG-2): each named registration declares its
/// own capability, so one configured address drops only that server's capability and the shared query
/// still answers for the live in-process server.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class NamedServerCapabilityTests
{
    [Test]
    public async Task TwoNamedServers_WhenOnlyAsAddressIsConfigured_ShouldKeepTheCapabilityForB()
    {
        var builder = BuilderWithBaseUrl("A");
        builder.AddAspNetCoreServer<SampleApi.Program>("A");
        builder.AddAspNetCoreServer<SampleApi.Program>("B");
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("two named servers", "00001", TestMethods.Placeholder);

        var first = context.TryServerFactory<SampleApi.Program>("A");
        var second = context.TryServerFactory<SampleApi.Program>("B");
        var capabilityIds = CapabilityIds(host);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Null, "A steps aside for its published address");
            Assert.That(second, Is.Not.Null, "B is live in-process");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Server),
                Is.True,
                "B's unsatisfied declaration keeps the server capability");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"), Is.True);
            Assert.That(
                capabilityIds,
                Is.EqualTo(new[] { "server:ASP.NET Core:B" }),
                "only the live server's instance is recorded");
        });
    }

    [Test]
    public async Task TwoNamedServers_WhenOnlyBsAddressIsConfigured_ShouldKeepTheCapabilityForA()
    {
        var builder = BuilderWithBaseUrl("B");
        builder.AddAspNetCoreServer<SampleApi.Program>("A");
        builder.AddAspNetCoreServer<SampleApi.Program>("B");
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("two named servers reversed", "00001", TestMethods.Placeholder);

        var first = context.TryServerFactory<SampleApi.Program>("A");
        var second = context.TryServerFactory<SampleApi.Program>("B");
        var capabilityIds = CapabilityIds(host);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null, "A is live in-process");
            Assert.That(second, Is.Null, "B steps aside for its published address");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Server),
                Is.True,
                "A's unsatisfied declaration keeps the server capability");
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"), Is.True);
            Assert.That(capabilityIds, Is.EqualTo(new[] { "server:ASP.NET Core:A" }));
        });
    }

    [Test]
    public async Task TwoNamedServers_WhenBothAddressesAreConfigured_ShouldDropTheCapabilityAndBothFactories()
    {
        var builder = BuilderWithBaseUrl("A", "B");
        builder.AddAspNetCoreServer<SampleApi.Program>("A");
        builder.AddAspNetCoreServer<SampleApi.Program>("B");
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("two published servers", "00001", TestMethods.Placeholder);

        var first = context.TryServerFactory<SampleApi.Program>("A");
        var second = context.TryServerFactory<SampleApi.Program>("B");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Null);
            Assert.That(second, Is.Null);
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Server),
                Is.False,
                "every server's declaration is satisfied, so no in-process server capability remains");
        });
    }

    [Test]
    public async Task TwoNamedServers_WhenNeitherAddressIsConfigured_ShouldCarryBothInstances()
    {
        var builder = BuilderWithBaseUrl();
        builder.AddAspNetCoreServer<SampleApi.Program>("A");
        builder.AddAspNetCoreServer<SampleApi.Program>("B");
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("two in-process servers", "00001", TestMethods.Placeholder);

        var first = context.TryServerFactory<SampleApi.Program>("A");
        var second = context.TryServerFactory<SampleApi.Program>("B");
        var capabilityIds = CapabilityIds(host);
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(
                capabilityIds,
                Is.EqualTo(new[] { "server:ASP.NET Core:A", "server:ASP.NET Core:B" }),
                "two named servers are two capabilities, not one collapsed descriptor");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"),
                Is.True,
                "the second argument matches the descriptor name");
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Server, "A"),
                Is.False,
                "the second argument never matches the instance (A1R-05)");
        });
    }

    private static string[] CapabilityIds(ProtoHost host)
        => [.. host.Trace.Snapshot().Entities!
            .Where(entity => entity.Kind == ProtoTraceEntityKinds.Capability)
            .Select(entity => entity.Id)
            .Order(StringComparer.Ordinal)];

    private static ProtoHostBuilder BuilderWithBaseUrl(params string[] configuredNames)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            configuredNames.ToDictionary(
                name => $"ProtoTest:Applications:{name}:BaseUrl",
                _ => (string?)"https://published.example.test")));
        return builder;
    }
}
