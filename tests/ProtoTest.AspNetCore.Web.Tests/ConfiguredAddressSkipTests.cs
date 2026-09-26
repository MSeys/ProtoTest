namespace ProtoTest.AspNetCore.Web.Tests;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Testcontainers;

/// <summary>
/// A piece registered with the configuration key it fills is skipped by
/// the host when the environment already configures that key, so the run points at the provided
/// environment instead of starting a local instance. The loopback test's create delegate and the
/// container test's <see cref="ProtoContainerResource{TContainer}.IsStarted"/> both fail the test if
/// the piece starts anyway; the container version stays hermetic because the skip decision is made
/// before Docker is ever contacted.
/// </summary>
[TestFixture]
public sealed class ConfiguredAddressSkipTests
{
    private const string PublishedAddress = "https://published.example.test";

    [Test]
    public async Task LoopbackApplication_WhenTheAddressIsConfigured_ShouldNotStartTheListener()
    {
        var builder = BuilderWithConfigured(BaseUrlKey(Setup.LoopbackApplicationName));
        builder.AddLoopbackApplication(
            Setup.LoopbackApplicationName,
            _ => throw new InvalidOperationException("The listener must not start when the address is configured."));
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
    }

    [Test]
    public async Task ApplicationContainer_WhenTheAddressIsConfigured_ShouldNotStartTheContainer()
    {
        var container = ApplicationContainer.Container(
            Setup.ContainerApplicationName, Setup.ContainerImage, Setup.ContainerPort);

        var builder = BuilderWithConfigured(container.BaseUrlKey);
        builder.AddInfrastructure(container, container.BaseUrlKey);
        await using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(container.IsStarted, Is.False, "the configured key satisfies the piece, so nothing starts it");
            Assert.That(container.ConnectionString, Is.Empty, "a skipped container publishes no address");
        });
    }

    private static ProtoHostBuilder BuilderWithConfigured(params string[] keys)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.Enabled = false);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            keys.ToDictionary(key => key, _ => (string?)PublishedAddress)));
        return builder;
    }

    private static string BaseUrlKey(string applicationName)
        => $"{ProtoApplication.SectionPath}:{applicationName}:BaseUrl";
}
