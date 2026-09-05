namespace ProtoTest.AspNetCore.Tests;

using System.Net;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

public class IntegrationTests
{
    [Test]
    public async Task AddAspNetCoreServer_Should_Initialize_InMemory_Server_And_HttpClient()
    {
        // 1. Arrange & Build ProtoHost
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();

        var context = host.BeginTestContext("InMemory_Test", "test-1", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // Execute pre-test hooks to initialize clients
            await host.ExecuteBeforeHooksAsync();

            // 2. Act: Call in-memory endpoint via Proto.Context
            var client = Proto.Context.Client<HttpClient>("Default");
            var response = await client.GetAsync("/ping");

            // 3. Assert
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            // Validate direct Server Service resolution
            var service = Proto.Context.ServerService<SampleApi.Program, SampleApi.ITestMessageService>("Default");
            Assert.That(service.GetMessage(), Is.EqualTo("Hello from AspNetCore DI!"));
        }
        finally
        {
            await host.ExecuteAfterHooksAsync();
            host.EndTestContext();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task AddAspNetCoreServer_Should_Use_External_Url_When_Configured()
    {
        // 1. Arrange: Provide Configuration override for the client BaseUrl
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "ProtoTest:Clients:ExternalApi:BaseUrl", "https://api.example.com" }
        };

        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(inMemoryConfig))
            .AddAspNetCoreServer<SampleApi.Program>("ExternalApi")
            .Build();

        var context = host.BeginTestContext("ExternalUrl_Test", "test-2", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            await host.ExecuteBeforeHooksAsync();

            // 2. Act
            var client = Proto.Context.Client<HttpClient>("ExternalApi");

            // 3. Assert: HttpClient should be configured with the external base address instead of in-memory WebApplicationFactory
            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://api.example.com")));
        }
        finally
        {
            await host.ExecuteAfterHooksAsync();
            host.EndTestContext();
            await host.DisposeAsync();
        }
    }
}