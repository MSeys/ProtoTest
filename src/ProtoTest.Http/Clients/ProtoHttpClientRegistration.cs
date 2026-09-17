namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Registers the named HTTP client and initializer shared by every HTTP-based protocol's
/// <c>AddClient</c> overloads, so each protocol only supplies its own display label and error text.
/// </summary>
public static class ProtoHttpClientRegistration
{
    /// <summary>Registers a named HTTP client with an explicit or application-resolved base URL.</summary>
    public static IProtoTargetBuilder AddClient(
        IServiceCollection services,
        string protocolName,
        string protocolLabel,
        string name,
        string? baseUrl,
        Action<IHttpClientBuilder>? configure,
        string? application = null,
        string? endpoint = null,
        bool allowMissingBaseUrl = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (baseUrl is not null && !ProtoHttpUri.TryCreateAbsoluteHttpUri(baseUrl, out _))
        {
            throw new ArgumentException(
                $"A {protocolLabel} client base URL must be an absolute HTTP or HTTPS URI.",
                nameof(baseUrl));
        }

        var httpClientBuilder = services.AddHttpClient(ProtoHttpClientInitializer.GetFactoryName(protocolName, name));
        configure?.Invoke(httpClientBuilder);
        services.AddSingleton<IProtoClientInitializer>(
            _ => new ProtoHttpClientInitializer(
                protocolName,
                name,
                baseUrl,
                allowMissingBaseUrl: allowMissingBaseUrl,
                application: application,
                endpoint: endpoint));
        services.AddSingleton(new ProtoApplicationTarget(name, application ?? name));

        return new ProtoHttpTargetBuilder(name, services);
    }

    /// <summary>Registers a named HTTP client whose absolute base address is resolved from per-test context.</summary>
    public static IProtoTargetBuilder AddClient(
        IServiceCollection services,
        string protocolName,
        string name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure,
        string? application = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(baseAddressResolver);

        var httpClientBuilder = services.AddHttpClient(ProtoHttpClientInitializer.GetFactoryName(protocolName, name));
        configure?.Invoke(httpClientBuilder);
        services.AddSingleton<IProtoClientInitializer>(
            _ => new ProtoHttpClientInitializer(protocolName, name, allowMissingBaseUrl: true));
        services.AddSingleton(new ProtoHttpBaseAddressRegistration(protocolName, name, baseAddressResolver));
        services.AddSingleton(new ProtoApplicationTarget(name, application ?? name));

        return new ProtoHttpTargetBuilder(name, services);
    }
}
