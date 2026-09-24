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

    public Task<bool> TryInitializeAsync(ProtoExecutionContext context, CancellationToken cancellationToken = default)
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

internal sealed class StaticConfigurationProvider(IReadOnlyDictionary<string, string?> values) : ConfigurationProvider
{
    public override void Load() => Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
}
