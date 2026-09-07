namespace ProtoTest.Rest.Tests;

using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using ProtoTest.Core;
using ProtoTest.Rest.Authenticators;
using System.Net.Http.Headers;

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
            Assert.That(exception.Message, Does.Contain("BaseUrl"));
        }
        finally
        {
            await host.CompleteTestAsync();
        }
    }

    [Test]
    public async Task BearerTokenAuthenticator_ShouldSetAuthorizationHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");

        await new BearerTokenAuthenticator("token").AuthenticateAsync(request);

        Assert.That(request.Headers.Authorization, Is.EqualTo(new AuthenticationHeaderValue("Bearer", "token")));
    }

    [Test]
    public async Task BasicAuthAuthenticator_ShouldEncodeCredentials()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.test");

        await new BasicAuthAuthenticator("user", "password").AuthenticateAsync(request);

        Assert.That(request.Headers.Authorization, Is.EqualTo(
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String("user:password"u8.ToArray()))));
    }

    [Test]
    public async Task ApiKeyAuthenticator_ShouldSupportHeaderAndQueryLocations()
    {
        using var headerRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.test");
        using var queryRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.test/items?existing=value");

        await new ApiKeyAuthenticator("X-Api-Key", "secret").AuthenticateAsync(headerRequest);
        await new ApiKeyAuthenticator("api_key", "secret", ApiKeyLocation.Query).AuthenticateAsync(queryRequest);

        Assert.That(headerRequest.Headers.GetValues("X-Api-Key").Single(), Is.EqualTo("secret"));
        Assert.That(queryRequest.RequestUri!.Query, Does.Contain("api_key=secret"));
        Assert.That(queryRequest.RequestUri.Query, Does.Contain("existing=value"));
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
