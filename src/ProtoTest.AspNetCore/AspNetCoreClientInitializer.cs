namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// Initializes an ASP.NET Core test server or falls back to an external URL override based on configuration.
/// </summary>
/// <typeparam name="TProgram">The entry point class of the ASP.NET Core application under test.</typeparam>
public sealed class AspNetCoreClientInitializer<TProgram>(
    string name,
    IConfiguration configuration,
    Action<WebApplicationFactory<TProgram>>? configureFactory = null)
    : IProtoClientInitializer where TProgram : class
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public Task InitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        // 1. Check for external URL override in configuration (e.g. appsettings.json or environment variables)
        var configUrl = configuration[$"ProtoTest:Clients:{Name}:BaseUrl"];

        HttpClient httpClient;

        if (!string.IsNullOrWhiteSpace(configUrl))
        {
            // OVERRIDE: Target an external/staging endpoint using a standard HttpClient
            httpClient = new HttpClient { BaseAddress = new Uri(configUrl) };
        }
        else
        {
            // DEFAULT: Spin up an in-memory WebApplicationFactory
            var factory = new WebApplicationFactory<TProgram>();
            configureFactory?.Invoke(factory);

            httpClient = factory.CreateClient();

            // Store WebApplicationFactory in context for proper async disposal after test completion
            context.RegisterClient(factory, $"{Name}:Factory");
        }

        // 2. Register the resulting HttpClient into the Core context
        context.RegisterClient(httpClient, Name);

        return Task.CompletedTask;
    }
}