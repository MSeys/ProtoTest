namespace ProtoTest.AspNetCore;

using Microsoft.AspNetCore.Mvc.Testing;
using ProtoTest.Core;

/// <summary>
/// Initializes an in-process ASP.NET Core test server and its client.
/// </summary>
/// <typeparam name="TProgram">The entry point class of the ASP.NET Core application under test.</typeparam>
public sealed class AspNetCoreClientInitializer<TProgram>(
    string name,
    Action<WebApplicationFactory<TProgram>>? configureFactory = null)
    : IProtoClientInitializer<HttpClient> where TProgram : class
{
    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
    {
        var factory = new WebApplicationFactory<TProgram>();
        context.RegisterClient(factory, $"{Name}:Factory");

        configureFactory?.Invoke(factory);

        context.RegisterClient(factory.CreateClient(), Name);
        return Task.FromResult(true);
    }
}
