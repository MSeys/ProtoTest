namespace ProtoTest.AspNetCore.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Pins the shared "ASP.NET Core" server descriptor across named registrations (audit REG-1/REG-2):
/// configuring one name's address drops the capability for every name, including a live in-process
/// server that is still serving.
/// </summary>
[TestFixture]
[Category("Characterization")]
public sealed class NamedServerCapabilityTests
{
    [Test]
    public async Task TwoNamedServers_WhenOnlyAsAddressIsConfigured_ShouldDropTheCapabilityWhileBStaysInProcess()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:A:BaseUrl"] = "https://published.example.test"
            }));
        builder.AddAspNetCoreServer<SampleApi.Program>("A");
        builder.AddAspNetCoreServer<SampleApi.Program>("B");
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("two named servers", "00001", TestMethods.Placeholder);

        var first = context.TryServerFactory<SampleApi.Program>("A");
        var second = context.TryServerFactory<SampleApi.Program>("B");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            // Pins current behavior; audit REG-1/REG-2 flip this once conditions are per declaration
            // and the descriptor carries the server name.
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Server),
                Is.False,
                "A's configured address removes the descriptor the live in-process server B shares");
            Assert.That(first, Is.Null, "A steps aside for its published address");
            Assert.That(second, Is.Not.Null, "B is live in-process even though the capability says otherwise");
        });
    }

    [Test]
    public async Task TwoNamedServers_WhenOnlyBsAddressIsConfigured_ShouldDropTheCapabilityWhileAStaysInProcess()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:B:BaseUrl"] = "https://published.example.test"
            }));
        builder.AddAspNetCoreServer<SampleApi.Program>("A");
        builder.AddAspNetCoreServer<SampleApi.Program>("B");
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("two named servers reversed", "00001", TestMethods.Placeholder);

        var first = context.TryServerFactory<SampleApi.Program>("A");
        var second = context.TryServerFactory<SampleApi.Program>("B");
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            // Pins current behavior; audit REG-1/REG-2 flip this once conditions are per declaration
            // and the descriptor carries the server name.
            Assert.That(
                host.HasCapability(ProtoCapabilityKinds.Server),
                Is.False,
                "B's configured address removes the descriptor the live in-process server A shares");
            Assert.That(first, Is.Not.Null, "A is live in-process even though the capability says otherwise");
            Assert.That(second, Is.Null, "B steps aside for its published address");
        });
    }
}
