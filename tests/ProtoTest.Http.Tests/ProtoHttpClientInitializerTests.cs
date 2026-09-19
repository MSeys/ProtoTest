namespace ProtoTest.Http.Tests;

using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

[TestFixture]
public sealed class ProtoHttpClientInitializerTests
{
    [TestCase("http://example.test")]
    [TestCase("https://example.test/path")]
    public void TryCreateAbsoluteHttpUri_ShouldAcceptHttpEndpoints(string value)
        => Assert.That(ProtoHttpUri.TryCreateAbsoluteHttpUri(value, out _), Is.True);

    [TestCase("/graphql")]
    [TestCase("ftp://example.test")]
    [TestCase("")]
    [TestCase("orders:search")]
    public void TryCreateAbsoluteHttpUri_ShouldRejectUnsupportedEndpoints(string value)
        => Assert.That(ProtoHttpUri.TryCreateAbsoluteHttpUri(value, out _), Is.False);

    [TestCase("orders:search", false)]
    [TestCase("/orders:search", false)]
    [TestCase("https://example.test", true)]
    [TestCase("custom+http://example.test", true)]
    public void HasExplicitScheme_ShouldRequireTheSchemeSeparator(string value, bool expected)
        => Assert.That(ProtoHttpUri.HasExplicitScheme(value), Is.EqualTo(expected));

    [Test]
    public async Task Initializer_ShouldPreferExplicitBaseUrlOverConfiguration()
    {
        await using var host = BuildHost("GraphQL",
            new ProtoHttpClientInitializer("GraphQL", "Catalog", "https://explicit.example/graphql"),
            "https://configured.example/graphql");
        await host.StartTestAsync("initializer", "1", TestMethod());
        try
        {
            Assert.That(Proto.Context.Client<HttpClient>("Catalog").BaseAddress,
                Is.EqualTo(new Uri("https://explicit.example/graphql")));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Initializer_ShouldUseCanonicalClientConfiguration()
    {
        await using var host = BuildHost("REST", new ProtoHttpClientInitializer("REST", "Catalog"),
            "https://configured.example/api/");
        await host.StartTestAsync("initializer", "2", TestMethod());
        try
        {
            Assert.That(Proto.Context.Client<HttpClient>("Catalog").BaseAddress,
                Is.EqualTo(new Uri("https://configured.example/api/")));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Initializer_ShouldUseTheTargetApplicationBaseUrl()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ProtoTest:Applications:Catalog:BaseUrl"] = "https://app.example/" }));
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient(ProtoHttpClientInitializer.GetFactoryName("REST", "Catalog"));
            services.AddSingleton<IProtoClientInitializer>(new ProtoHttpClientInitializer("REST", "Catalog"));
        });
        await using var host = builder.Build();
        await host.StartTestAsync("initializer", "4", TestMethod());
        try
        {
            Assert.That(Proto.Context.Client<HttpClient>("Catalog").BaseAddress,
                Is.EqualTo(new Uri("https://app.example/")));
        }
        finally { await host.CompleteTestAsync(); }
    }

    [Test]
    public async Task Initializer_ShouldFailForInvalidConfiguredBaseUrl()
    {
        await using var host = BuildHost("GraphQL", new ProtoHttpClientInitializer("GraphQL", "Catalog"), "/graphql");
        var exception = Assert.ThrowsAsync<InvalidOperationException>(() =>
            host.StartTestAsync("initializer", "3", TestMethod()));
        Assert.That(exception!.Message, Does.Contain("GraphQL client 'Catalog'"));
    }

    [Test]
    public void FactoryNames_ShouldBeIsolatedPerProtocolAndClient()
    {
        Assert.That(ProtoHttpClientInitializer.GetFactoryName("GraphQL", "Catalog"),
            Is.Not.EqualTo(ProtoHttpClientInitializer.GetFactoryName("REST", "Catalog")));
        Assert.That(ProtoHttpClientInitializer.GetFactoryName("GraphQL", "Catalog"),
            Is.Not.EqualTo(ProtoHttpClientInitializer.GetFactoryName("GraphQL", "Billing")));
    }

    private static ProtoHost BuildHost(string protocol, IProtoClientInitializer initializer, string configuredBaseUrl)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["ProtoTest:Applications:Catalog:BaseUrl"] = configuredBaseUrl }));
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient(ProtoHttpClientInitializer.GetFactoryName(protocol, "Catalog"));
            services.AddSingleton(initializer);
        });
        return builder.Build();
    }

    private static MethodInfo TestMethod() => typeof(ProtoHttpClientInitializerTests)
        .GetMethod(nameof(Dummy), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static void Dummy() { }
}
