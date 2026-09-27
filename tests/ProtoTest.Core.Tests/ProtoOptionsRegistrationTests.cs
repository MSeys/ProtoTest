namespace ProtoTest.Core.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Pins the keyed per-client registration: two names never share options, one name's callbacks compose
/// in registration order, the section binds over them, and the optional unkeyed default is the instance
/// without any name's callbacks.
/// </summary>
[TestFixture]
public sealed class ProtoOptionsRegistrationTests
{
    private static IConfiguration EmptyConfiguration => new ConfigurationBuilder().Build();

    [Test]
    public void ConfigureKeyed_ShouldIsolateNamesAndComposeCallbacksInOrder()
    {
        var services = new ServiceCollection().AddSingleton(EmptyConfiguration);
        ProtoOptionsRegistration.ConfigureKeyed(
            services, "Alpha", () => new TestOptions { Value = "default" },
            options => options.Tags.Add("first"));
        ProtoOptionsRegistration.ConfigureKeyed(
            services, "Alpha", () => new TestOptions { Value = "default" },
            options => options.Tags.Add("second"));
        ProtoOptionsRegistration.ConfigureKeyed(
            services, "Beta", () => new TestOptions { Value = "default" },
            options => options.Value = "beta");
        using var provider = services.BuildServiceProvider();

        var alpha = provider.GetRequiredKeyedService<TestOptions>("Alpha");
        var beta = provider.GetRequiredKeyedService<TestOptions>("Beta");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(alpha.Tags, Is.EqualTo(new[] { "first", "second" }), "one name's callbacks compose in order");
            Assert.That(alpha.Value, Is.EqualTo("default"), "another name's callback does not leak");
            Assert.That(beta.Value, Is.EqualTo("beta"));
            Assert.That(beta.Tags, Is.Empty, "another name's callbacks do not leak");
        }
    }

    [Test]
    public void ConfigureKeyed_ShouldBindTheSectionOverTheCodeCallbacks()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ProtoTest:TestOptions:Value"] = "configured"
            })
            .Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(configuration);
        ProtoOptionsRegistration.ConfigureKeyed(
            services, "Alpha", () => new TestOptions { Value = "code" },
            options => options.Value = "callback");
        using var provider = services.BuildServiceProvider();

        var alpha = provider.GetRequiredKeyedService<TestOptions>("Alpha");

        Assert.That(alpha.Value, Is.EqualTo("configured"), "configuration binds over the code callback");
    }

    [Test]
    public void ConfigureKeyed_ShouldRegisterTheUnkeyedDefaultWithoutNamedCallbacks()
    {
        var services = new ServiceCollection().AddSingleton(EmptyConfiguration);
        ProtoOptionsRegistration.ConfigureKeyed(
            services, "Alpha", () => new TestOptions { Value = "default" },
            options => options.Value = "alpha",
            registerDefault: true);
        using var provider = services.BuildServiceProvider();

        var keyed = provider.GetRequiredKeyedService<TestOptions>("Alpha");
        var unkeyed = provider.GetRequiredService<TestOptions>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keyed.Value, Is.EqualTo("alpha"));
            Assert.That(unkeyed.Value, Is.EqualTo("default"), "the run-wide default carries no named client's callback");
        }
    }

    [Test]
    public void ConfigureKeyed_ShouldNotRegisterAnUnkeyedDefaultByDefault()
    {
        var services = new ServiceCollection().AddSingleton(EmptyConfiguration);
        ProtoOptionsRegistration.ConfigureKeyed(services, "Alpha", () => new TestOptions());
        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetService<TestOptions>(), Is.Null);
        Assert.That(provider.GetRequiredKeyedService<TestOptions>("Alpha"), Is.Not.Null);
    }

    private sealed class TestOptions : IProtoConfigurableOptions
    {
        public string? Value { get; set; }

        public List<string> Tags { get; } = [];

        public string ConfigurationSectionName => "ProtoTest:TestOptions";
    }
}
