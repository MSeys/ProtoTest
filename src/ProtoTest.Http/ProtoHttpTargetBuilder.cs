namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>Shared, protocol-neutral <see cref="IProtoTargetBuilder"/> for HTTP-based protocol integrations.</summary>
public sealed class ProtoHttpTargetBuilder(string targetName, IServiceCollection services) : IProtoTargetBuilder
{
    public string TargetName { get; } = targetName;
    public IServiceCollection Services { get; } = services;
}

/// <summary>
/// Registers the named HTTP client and initializer shared by every HTTP-based protocol's
/// <c>AddClient</c> overloads, so each protocol only supplies its own display label and error text.
/// </summary>
public static class ProtoHttpClientRegistration
{
    /// <summary>Registers a named HTTP client with an explicit or configuration-resolved base URL.</summary>
    public static IProtoTargetBuilder AddClient(
        IServiceCollection services,
        string protocolName,
        string protocolLabel,
        string name,
        string? baseUrl,
        Action<IHttpClientBuilder>? configure)
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
            _ => new ProtoHttpClientInitializer(protocolName, name, baseUrl));

        return new ProtoHttpTargetBuilder(name, services);
    }

    /// <summary>Registers a named HTTP client whose absolute base address is resolved from per-test context.</summary>
    public static IProtoTargetBuilder AddClient(
        IServiceCollection services,
        string protocolName,
        string name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(baseAddressResolver);

        var httpClientBuilder = services.AddHttpClient(ProtoHttpClientInitializer.GetFactoryName(protocolName, name));
        configure?.Invoke(httpClientBuilder);
        services.AddSingleton<IProtoClientInitializer>(
            _ => new ProtoHttpClientInitializer(protocolName, name, allowMissingBaseUrl: true));
        services.AddSingleton(new ProtoHttpBaseAddressRegistration(protocolName, name, baseAddressResolver));

        return new ProtoHttpTargetBuilder(name, services);
    }
}
