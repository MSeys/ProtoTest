namespace ProtoTest.Rest.Tests;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Http;
using ProtoTest.Http.Authenticators;
using System.Net.Http.Headers;
using System.Reflection;

[TestFixture]
public class RestConfigurationAndAuthenticationTests
{
    [Test]
    public async Task AddClient_ShouldResolveBaseUrlFromConfiguration()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.Add(
            new StaticConfigurationSource(new Dictionary<string, string?>
            {
                ["ProtoTest:Clients:Orders:BaseUrl"] = "https://configured.example/"
            })));
        builder.AddRest(rest => rest.AddClient("Orders"));
        await using var host = builder.Build();

        // Act
        await host.StartTestAsync("ConfiguredClient", "00001", (System.Reflection.MethodInfo)System.Reflection.MethodInfo.GetCurrentMethod()!);

        try
        {
            var client = Proto.Context.Client<HttpClient>("Orders");

            // Assert
            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://configured.example/")));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task AddClient_ShouldPreferExplicitBaseUrlOverConfiguration()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.Add(
            new StaticConfigurationSource(new Dictionary<string, string?>
            {
                ["ProtoTest:Clients:Orders:BaseUrl"] = "https://configured.example/"
            })));
        builder.AddRest(rest => rest.AddClient("Orders", "https://explicit.example/"));
        await using var host = builder.Build();

        // Act
        await host.StartTestAsync("ExplicitClient", "00002", (System.Reflection.MethodInfo)System.Reflection.MethodInfo.GetCurrentMethod()!);

        try
        {
            var client = Proto.Context.Client<HttpClient>("Orders");

            // Assert
            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://explicit.example/")));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task AddClient_ShouldThrowWhenBaseUrlIsMissing()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddRest(rest => rest.AddClient("Orders"));
        await using var host = builder.Build();
        // Act & Assert
        try
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await host.StartTestAsync("MissingClient", "00003", (System.Reflection.MethodInfo)System.Reflection.MethodInfo.GetCurrentMethod()!));
            Assert.That(exception!.Message, Does.Contain("Orders"));
            Assert.That(exception.Message, Does.Contain(nameof(HttpClient)));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task CaptureAttachments_ShouldLetKnownConfigurationSectionOverrideCodeDefaults()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureAppConfiguration(configuration => configuration.Add(
            new StaticConfigurationSource(new Dictionary<string, string?>
            {
                ["ProtoTest:Rest:Attachments:CaptureRequestBodies"] = "false",
                ["ProtoTest:Rest:Attachments:CaptureResponses"] = "true",
                ["ProtoTest:Rest:Attachments:CaptureExpectedShapes"] = "false"
            })));
        builder.AddRest(rest => rest.CaptureAttachments(options =>
        {
            options.CaptureResponses = false;
            options.CaptureExpectedShapes = true;
        }));
        await using var host = builder.Build();
        await host.StartTestAsync(
            "AttachmentConfiguration",
            "00004",
            (System.Reflection.MethodInfo)System.Reflection.MethodInfo.GetCurrentMethod()!);

        try
        {
            var options = Proto.Context.Service<RestAttachmentOptions>();

            Assert.That(options.CaptureRequestBodies, Is.False);
            Assert.That(options.CaptureResponses, Is.True);
            Assert.That(options.CaptureExpectedShapes, Is.False);
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public void AddClient_ShouldRejectNonHttpBaseUrl()
    {
        var builder = new ProtoHostBuilder();

        var exception = Assert.Throws<ArgumentException>(() =>
            builder.AddRest(rest => rest.AddClient("Files", "file:///temporary/api")));

        Assert.That(exception!.Message, Does.Contain("HTTP or HTTPS"));
    }

    [Test]
    public async Task BearerTokenAuthenticator_ShouldSetAuthorizationHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");

        await AuthenticateAsync(new BearerTokenAuthenticator("token"), request);

        Assert.That(request.Headers.Authorization, Is.EqualTo(new AuthenticationHeaderValue("Bearer", "token")));
    }

    [Test]
    public async Task BasicAuthAuthenticator_ShouldEncodeCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");

        await AuthenticateAsync(new BasicAuthAuthenticator("user", "password"), request);

        Assert.That(request.Headers.Authorization, Is.EqualTo(
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String("user:password"u8.ToArray()))));
    }

    [Test]
    public async Task ApiKeyAuthenticator_ShouldSupportHeaderAndQueryLocations()
    {
        using var headerRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        using var queryRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.test/items?existing=value");

        await AuthenticateAsync(new ApiKeyAuthenticator("X-Api-Key", "secret"), headerRequest);
        await AuthenticateAsync(new ApiKeyAuthenticator("api_key", "secret", ApiKeyLocation.Query), queryRequest);

        Assert.That(headerRequest.Headers.GetValues("X-Api-Key").Single(), Is.EqualTo("secret"));
        Assert.That(queryRequest.RequestUri!.Query, Does.Contain("api_key=secret"));
        Assert.That(queryRequest.RequestUri.Query, Does.Contain("existing=value"));
    }

    [Test]
    public async Task ApiKeyAuthenticator_ShouldSupportRelativeUrisAndReplaceExistingValues()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/items?api_key=old#results");

        await AuthenticateAsync(
            new ApiKeyAuthenticator("api_key", "new secret", ApiKeyLocation.Query),
            request);

        Assert.That(request.RequestUri!.OriginalString, Is.EqualTo("/items?api_key=new%20secret#results"));
    }

    private static async Task AuthenticateAsync(
        IProtoHttpAuthenticator authenticator,
        HttpRequestMessage request)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var context = new ProtoExecutionContext(
            "Authenticator unit test",
            services.CreateScope(),
            "00000",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);
        await authenticator.AuthenticateAsync(
            new ProtoHttpAuthenticationContext(request, context, "Default"));
    }

    private sealed class StaticConfigurationSource(IReadOnlyDictionary<string, string?> values) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
            => new StaticConfigurationProvider(values);
    }

    private sealed class StaticConfigurationProvider(IReadOnlyDictionary<string, string?> values)
        : ConfigurationProvider
    {
        public override void Load()
        {
            Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
        }
    }
}
