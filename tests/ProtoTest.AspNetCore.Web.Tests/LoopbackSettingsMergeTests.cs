namespace ProtoTest.AspNetCore.Web.Tests;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using ProtoTest.AspNetCore;
using ProtoTest.Core;

/// <summary>
/// The loopback listener forwards the run's collected configuration into the hand-built
/// application: the suite's configuration first, then the values the pieces started before it
/// published, so a container address a factory needs is visible inside the application's own
/// configuration. The factory must pass its arguments to its builder for the merge to apply.
/// </summary>
[TestFixture]
public sealed class LoopbackSettingsMergeTests
{
    private const string FromConfigKey = "LoopbackMerge:FromConfig";
    private const string FromSettingsKey = "LoopbackMerge:FromSettings";
    private const string SharedKey = "LoopbackMerge:Shared";

    [Test]
    public async Task AddLoopbackApplication_ShouldForwardConfigurationAndEarlierSettingsIntoTheApplication()
    {
        string? fromConfig = null;
        string? fromSettings = null;
        string? shared = null;
        string? urls = null;

        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                [FromConfigKey] = "config-value",
                [SharedKey] = "config-value"
            }));
        builder.AddInfrastructure(
            "LoopbackMergeSettings",
            chain => chain.Use(new ProtoTargetProvider("settings", new StubSettingsInfrastructure())),
            FromSettingsKey,
            SharedKey);
        builder.AddLoopbackApplication("Merge", args =>
        {
            var application = WebApplication.CreateBuilder(args).Build();
            fromConfig = application.Configuration[FromConfigKey];
            fromSettings = application.Configuration[FromSettingsKey];
            shared = application.Configuration[SharedKey];
            urls = application.Configuration["urls"];
            return application;
        });

        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fromConfig, Is.EqualTo("config-value"), "the suite's configuration reaches the application");
            Assert.That(fromSettings, Is.EqualTo("settings-value"), "an earlier piece's settings reach the application");
            Assert.That(shared, Is.EqualTo("settings-win"), "published settings win over suite configuration");
            Assert.That(urls, Is.EqualTo("http://127.0.0.1:0"), "the loopback listener keeps port 0");
        }
    }

    private sealed class StubSettingsInfrastructure : IProtoSettingsInfrastructure
    {
        public string Id => "loopback-merge:stub";

        public string Kind => "test";

        public string Description => "Settings stub for the loopback merge test.";

        public ProtoResourceScope Scope => ProtoResourceScope.Run;

        public IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FromSettingsKey] = "settings-value",
            [SharedKey] = "settings-win"
        };

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
    }
}
