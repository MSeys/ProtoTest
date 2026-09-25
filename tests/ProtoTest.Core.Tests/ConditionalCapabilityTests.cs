namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;

[TestFixture]
public sealed class ConditionalCapabilityTests
{
    private static readonly ProtoCapabilityDescriptor Capability =
        new("ASP.NET Core", ProtoCapabilityKinds.Server, "Tests");

    [Test]
    public async Task AddCapabilityUnlessConfigured_WhenEveryKeyIsConfigured_ShouldDropTheCapability()
    {
        var output = Path.Combine(Path.GetTempPath(), $"prototest-capability-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options => options.OutputPath = output);
            builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ProtoTest:Applications:Api:BaseUrl"] = "https://staging"
                }));
            builder.AddCapabilityUnlessConfigured(Capability, "ProtoTest:Applications:Api:BaseUrl");
            await using var host = builder.Build();
            await host.StartAsync();
            await host.StartTestAsync("dropped capability", "00001", TestMethods.Placeholder);
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.StopAsync();

            var snapshot = host.Trace.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(
                    host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"),
                    Is.False,
                    "the environment provides what the integration would");
                Assert.That(
                    snapshot.Entities!.Any(entity => entity.Kind == ProtoTraceEntityKinds.Capability
                        && entity.Name == "ASP.NET Core"),
                    Is.False,
                    "the run overview does not show a capability that is not there");
                Assert.That(
                    snapshot.Entries!.Any(entry => entry.Kind == "capability.skipped"),
                    Is.True,
                    "the trace records the decision");
            });
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task AddCapabilityUnlessConfigured_WhenAKeyIsMissing_ShouldKeepTheCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.AddCapabilityUnlessConfigured(Capability, "ProtoTest:Applications:Api:BaseUrl");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        var snapshot = host.Trace.Snapshot();
        Assert.Multiple(() =>
        {
            Assert.That(host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"), Is.True);
            Assert.That(
                snapshot.Entities!.Any(entity => entity.Kind == ProtoTraceEntityKinds.Capability
                    && entity.Name == "ASP.NET Core"),
                Is.True);
        });
    }

    [Test]
    public async Task AddCapabilityUnlessConfigured_WhenOnlySomeKeysAreConfigured_ShouldKeepTheCapability()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "https://staging"
            }));
        builder.AddCapabilityUnlessConfigured(
            Capability,
            "ProtoTest:Applications:Api:BaseUrl",
            "ProtoTest:Applications:Api:SecondaryUrl");
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.That(host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"), Is.True);
    }

    [Test]
    public async Task AddCapability_APlainRegistrationForTheSameDescriptor_ShouldWin()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ProtoTest:Applications:Api:BaseUrl"] = "https://staging"
            }));
        builder.AddCapabilityUnlessConfigured(Capability, "ProtoTest:Applications:Api:BaseUrl");
        builder.AddCapability(Capability);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.That(
            host.HasCapability(ProtoCapabilityKinds.Server, "ASP.NET Core"),
            Is.True,
            "an unconditional declaration is a promise the environment cannot withdraw");
    }
}
