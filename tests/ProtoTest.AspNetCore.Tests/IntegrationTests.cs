namespace ProtoTest.AspNetCore.Tests;

using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Rest;

public class IntegrationTests
{
    [Test]
    public async Task AddAspNetCoreServer_Should_Initialize_InMemory_Server_And_HttpClient()
    {
        // 1. Arrange & Build ProtoHost
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>("Default")
            .Build();

        var context = await host.StartTestAsync("InMemory_Test", "00001", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
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
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task Rest_First_Should_Use_External_Client_When_BaseUrl_Is_Configured()
    {
        // 1. Arrange: Provide Configuration override for the client BaseUrl
        var inMemoryConfig = new Dictionary<string, string?>
        {
            { "ProtoTest:Applications:ExternalApi:BaseUrl", "https://api.example.com" }
        };

        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(inMemoryConfig))
            .AddRest(rest => rest.AddClient("ExternalApi"))
            .AddAspNetCoreServer<SampleApi.Program>("ExternalApi")
            .Build();

        var context = await host.StartTestAsync("ExternalUrl_Test", "00002", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // 2. Act
            var client = Proto.Context.Client<HttpClient>("ExternalApi");

            // 3. Assert: REST handled the client before the ASP.NET Core fallback was reached.
            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://api.example.com")));
            Assert.That(
                Proto.Context.TryClient<WebApplicationFactory<SampleApi.Program>>("ExternalApi:Factory"),
                Is.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task Rest_First_Should_Fall_Through_To_AspNetCore_When_BaseUrl_Is_Missing()
    {
        var host = new ProtoHostBuilder()
            .AddRest(rest => rest.AddClient("OrderApi"))
            .AddAspNetCoreServer<SampleApi.Program>("OrderApi")
            .Build();

        await host.StartTestAsync("LocalFallback_Test", "00004", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            var response = await Proto.Context.Rest("OrderApi").GetAsync("/ping");

            response.ShouldHaveHttpStatus(HttpStatusCode.OK);
            Assert.That(Proto.Context.ServerFactory<SampleApi.Program>("OrderApi"), Is.Not.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task AspNetCore_First_Should_Take_Precedence_Over_Configured_Rest_Client()
    {
        var inMemoryConfig = new Dictionary<string, string?>
        {
            ["ProtoTest:Applications:OrderApi:BaseUrl"] = "https://api.example.com"
        };

        var host = new ProtoHostBuilder()
            .ConfigureAppConfiguration(builder => builder.AddInMemoryCollection(inMemoryConfig))
            .AddAspNetCoreServer<SampleApi.Program>("OrderApi")
            .AddRest(rest => rest.AddClient("OrderApi"))
            .Build();

        await host.StartTestAsync("RegistrationOrder_Test", "00005", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            var response = await Proto.Context.Client<HttpClient>("OrderApi").GetAsync("/ping");

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Proto.Context.ServerFactory<SampleApi.Program>("OrderApi"), Is.Not.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task AddAspNetCoreServer_Should_Invoke_FactoryConfigurationCallback()
    {
        // Arrange
        var callbackInvoked = false;
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "ConfiguredApi",
                webHost =>
                {
                    callbackInvoked = true;
                    webHost.UseSetting("ProtoTest:Configured", "true");
                })
            .Build();

        await host.StartTestAsync("FactoryConfiguration", "00003", (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            // Act
            // Assert
            Assert.That(callbackInvoked, Is.True);
            Assert.That(Proto.Context.Client<HttpClient>("ConfiguredApi"), Is.Not.Null);
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task AddAspNetCoreServer_ShouldApplyWebHostServiceOverridesAndClientOptions()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "ConfiguredApi",
                webHost => webHost.ConfigureTestServices(services =>
                {
                    services.RemoveAll<SampleApi.ITestMessageService>();
                    services.AddSingleton<SampleApi.ITestMessageService, ReplacementMessageService>();
                }),
                client => client.BaseAddress = new Uri("https://configured.example.test"))
            .Build();

        await host.StartTestAsync(
            "ConfiguredServices",
            "00006",
            (MethodInfo)MethodInfo.GetCurrentMethod()!);

        try
        {
            var client = Proto.Context.Client<HttpClient>("ConfiguredApi");
            using var response = await client.GetAsync("/message");
            var body = await response.Content.ReadAsStringAsync();

            Assert.That(client.BaseAddress, Is.EqualTo(new Uri("https://configured.example.test")));
            Assert.That(body, Does.Contain("replacement"));
        }
        finally
        {
            await host.CompleteTestAsync();
            await host.DisposeAsync();
        }
    }

    [Test]
    public async Task PerRunLifetime_ShouldShareOneApplicationAcrossTests_AndDisposeItWithTheHost()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "SharedApi",
                webHost => webHost.UseSetting("ProtoTest:Configured", "true"))
            .Build();
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

        await host.StartTestAsync("First", "00007", method);
        var first = Proto.Context.ServerFactory<SampleApi.Program>("SharedApi");
        using (var response = await Proto.Context.Client<HttpClient>("SharedApi").GetAsync("/ping"))
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        await host.StartTestAsync("Second", "00008", method);
        var second = Proto.Context.ServerFactory<SampleApi.Program>("SharedApi");
        using (var response = await Proto.Context.Client<HttpClient>("SharedApi").GetAsync("/ping"))
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
        var initializations = host.Trace.Snapshot().Tests
            .SelectMany(test => test.Entries)
            .Where(entry => entry.Kind == "aspnetcore.server.initialize")
            .Select(entry => entry.Attributes["server.reused"])
            .ToArray();
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.SameAs(first));
            Assert.That(initializations, Is.EqualTo(new[] { "false", "true" }));
            Assert.That(ResolveMessage(first), Is.EqualTo("Hello from AspNetCore DI!"));
        });

        await host.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => ResolveMessage(first));
    }

    [Test]
    public async Task PerTestLifetime_ShouldStartAndDisposeAnApplicationForEachTest()
    {
        var host = new ProtoHostBuilder()
            .AddAspNetCoreServer<SampleApi.Program>(
                "IsolatedApi",
                webHost => webHost.UseSetting("ProtoTest:Configured", "true"),
                lifetime: AspNetCoreServerLifetime.PerTest)
            .Build();
        await using var ownedHost = host;
        var method = (MethodInfo)MethodInfo.GetCurrentMethod()!;

        await host.StartTestAsync("First", "00009", method);
        var first = Proto.Context.ServerFactory<SampleApi.Program>("IsolatedApi");
        Assert.That(ResolveMessage(first), Is.EqualTo("Hello from AspNetCore DI!"));
        await host.CompleteTestAsync(ProtoTestResult.Passed);

        Assert.Throws<ObjectDisposedException>(() => ResolveMessage(first));

        await host.StartTestAsync("Second", "00010", method);
        var second = Proto.Context.ServerFactory<SampleApi.Program>("IsolatedApi");
        Assert.Multiple(() =>
        {
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(ResolveMessage(second), Is.EqualTo("Hello from AspNetCore DI!"));
        });
        await host.CompleteTestAsync(ProtoTestResult.Passed);
    }

    private static string ResolveMessage(WebApplicationFactory<SampleApi.Program> factory)
        => factory.Services.GetRequiredService<SampleApi.ITestMessageService>().GetMessage();

    private sealed class ReplacementMessageService : SampleApi.ITestMessageService
    {
        public string GetMessage() => "replacement";
    }
}
