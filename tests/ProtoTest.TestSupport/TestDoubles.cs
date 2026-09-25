namespace ProtoTest.TestSupport;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>Answers every request with the response the test builds, without a network.</summary>
public sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(respond(request));
}

/// <summary>Registers an HTTP client over a stub handler, so a target resolves without a network.</summary>
public sealed class StubTransportInitializer(string name, string baseAddress, HttpMessageHandler handler)
    : IProtoClientInitializer<HttpClient>
{
    public string Name { get; } = name;

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context)
    {
        context.RegisterClient(new HttpClient(handler) { BaseAddress = new Uri(baseAddress) }, Name);
        return Task.FromResult(true);
    }
}

/// <summary>Supplies configuration from a fixed dictionary instead of a file or environment.</summary>
public sealed class StaticConfigurationSource(IReadOnlyDictionary<string, string?> values) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new StaticConfigurationProvider(values);
}

/// <summary>
/// Publishes one application's base URL as a settings piece - the shape the demo uses for a standalone
/// process the run starts, and the address every application-scoped reader resolves.
/// </summary>
public sealed class PublishedAddressInfrastructure(string application, string address) : IProtoSettingsInfrastructure
{
    public string Id => $"address:{application}";

    public string Kind => "address";

    public string Description => $"Published address · {application}";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public IReadOnlyDictionary<string, string> Settings { get; } = new Dictionary<string, string>
    {
        [$"ProtoTest:Applications:{application}:BaseUrl"] = address
    };

    public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
}

/// <summary>
/// Declares and fills one configuration key when it starts - the shape a container or another run piece
/// has for the key an integration reads its address from.
/// </summary>
public sealed class DeclaredSettingsInfrastructure(string id, string key, string value) : IProtoSettingsInfrastructure
{
    public string Id => id;

    public string Kind => "settings";

    public string Description => $"Declared settings · {key}";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public IReadOnlyDictionary<string, string> Settings { get; } = new Dictionary<string, string>
    {
        [key] = value
    };

    public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
}

internal sealed class StaticConfigurationProvider(IReadOnlyDictionary<string, string?> values) : ConfigurationProvider
{
    public override void Load() => Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
}
