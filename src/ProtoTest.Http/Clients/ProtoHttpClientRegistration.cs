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
        => applicationName is null ? name : $"{applicationName}:{name}";

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
    /// Registers a target whose transport comes from another HTTP client, such as one registered by
    /// an in-process ASP.NET Core server, optionally rooted at a path. Every HTTP-based protocol's
    /// <c>AddClientFrom</c> overload uses this so aliasing behaves identically.
    /// </summary>
    public static IProtoTargetBuilder AddClientFrom(
        IServiceCollection services,
        string protocolName,
        string name,
        string sourceClientName,
        string? basePath = null,
        string? application = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceClientName);

        // Application clients register in the test context under their qualified name, so an alias
        // inside AddApplication resolves the source as {application}:{source} first and falls back to
        // the raw name for a source registered directly.
        var qualifiedSourceName = Qualify(sourceClientName, application);
        services.AddSingleton(new ProtoApplicationTarget(name, application ?? name));
        services.AddSingleton(new ProtoHttpClientAliasRegistration(
            protocolName,
            name,
            qualifiedSourceName,
            (context, _) =>
            {
                var source = context.TryClient<HttpClient>(qualifiedSourceName)
                    ?? (string.Equals(qualifiedSourceName, sourceClientName, StringComparison.Ordinal)
                        ? null
                        : context.TryClient<HttpClient>(sourceClientName))
                    ?? context.Client<HttpClient>(sourceClientName);
                var baseAddress = source.BaseAddress
                    ?? throw new InvalidOperationException($"HTTP client '{sourceClientName}' has no base address.");
                return ValueTask.FromResult(
                    string.IsNullOrWhiteSpace(basePath) ? baseAddress : new Uri(baseAddress, basePath));
            }));
        return new ProtoTargetBuilder(name, services);
    }
}
