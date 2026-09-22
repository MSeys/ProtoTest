namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Registers the named HTTP client and initializer shared by every HTTP-based protocol's
/// <c>AddClient</c> overloads, so each protocol only supplies its own display label and error text.
/// </summary>
public static class ProtoHttpClientRegistration
{
    /// <summary>Qualifies a client name with its application, so per-application clients never collide.</summary>
    public static string Qualify(string name, string? applicationName)
        => ProtoClientResolution.Qualify(name, applicationName);

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
        services.AddSingleton(new ProtoHttpClientEndpointRegistration(name, endpoint));
        services.AddSingleton(new ProtoApplicationTarget(name, application ?? name));

        return new ProtoTargetBuilder(name, services);
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

        return new ProtoTargetBuilder(name, services);
    }

    /// <summary>
    /// The protocol builders' shared first step for a named client: default the name, qualify it with
    /// the application, register it with that application, and hand off to
    /// <see cref="AddClient(IServiceCollection, string, string, string, string?, Action{IHttpClientBuilder}?, string?, string?, bool)"/>.
    /// </summary>
    internal static IProtoTargetBuilder AddNamedClient(
        IServiceCollection services,
        string protocolName,
        string protocolLabel,
        IProtoApplicationBuilder? application,
        string? name,
        string? baseUrl,
        Action<IHttpClientBuilder>? configure,
        string? endpoint)
    {
        var clientName = name ?? "Default";
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        var applicationName = application?.ApplicationName;
        var registeredName = Qualify(clientName, applicationName);
        application?.RegisterClient(protocolName, clientName);

        return AddClient(
            services,
            protocolName,
            protocolLabel,
            registeredName,
            baseUrl,
            configure,
            applicationName,
            endpoint,
            // Under an application with no configured URL, the client reuses the application's
            // in-process transport (see AddAspNetCoreServer) or its BaseUrl.
            allowMissingBaseUrl: applicationName is not null && baseUrl is null);
    }

    /// <summary>The same shared first step for a client whose base address comes from per-test context.</summary>
    internal static IProtoTargetBuilder AddResolvedClient(
        IServiceCollection services,
        string protocolName,
        IProtoApplicationBuilder? application,
        string? name,
        Func<ProtoExecutionContext, CancellationToken, ValueTask<Uri>> baseAddressResolver,
        Action<IHttpClientBuilder>? configure)
    {
        var clientName = name ?? "Default";
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        var applicationName = application?.ApplicationName;
        var registeredName = Qualify(clientName, applicationName);
        application?.RegisterClient(protocolName, clientName);

        return AddClient(services, protocolName, registeredName, baseAddressResolver, configure, applicationName);
    }
}
