namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using ProtoTest.Core;

/// <summary>
/// Initializes an in-process ASP.NET Core test server and its client.
/// </summary>
/// <typeparam name="TProgram">The entry point class of the ASP.NET Core application under test.</typeparam>
internal sealed class AspNetCoreClientInitializer<TProgram>(
    string name,
    Action<IWebHostBuilder>? configureWebHost = null,
    Action<WebApplicationFactoryClientOptions>? configureClient = null)
    : IProtoClientInitializer<HttpClient> where TProgram : class
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        WebApplicationFactory<TProgram> factory = new();
        if (configureWebHost is not null)
        {
            factory = factory.WithWebHostBuilder(configureWebHost);
        }

        context.RegisterClient(factory, $"{Name}:Factory");

        var clientOptions = new WebApplicationFactoryClientOptions();
        configureClient?.Invoke(clientOptions);
        context.RegisterClient(factory.CreateClient(clientOptions), Name);
        return Task.FromResult(true);
    }
}
