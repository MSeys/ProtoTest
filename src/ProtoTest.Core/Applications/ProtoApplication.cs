namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;

/// <summary>
/// Resolves the named application — a system under test with a base address and protocol descriptors —
/// that a client or web session targets, and reads values from its <c>ProtoTest:Applications:{app}</c>
/// section. A scope selects an application with its own <c>Application</c> setting, defaulting to the
/// scope's own name, so a REST client, GraphQL client, and browser session can share one application.
/// </summary>
public static class ProtoApplication
{
    /// <summary>The configuration prefix under which named applications are declared.</summary>
    public const string SectionPath = "ProtoTest:Applications";

    /// <summary>
    /// Returns the application a scope targets: its <c>{scopeSection}:Application</c> value, or
    /// <paramref name="fallback"/> (normally the client or session name) when unset.
    /// </summary>
    public static string ResolveName(IConfiguration configuration, string scopeSection, string fallback)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeSection);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);
        var configured = configuration[$"{scopeSection}:Application"];
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }

    /// <summary>Returns the configuration section that describes an application.</summary>
    public static IConfigurationSection Section(IConfiguration configuration, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return configuration.GetSection($"{SectionPath}:{applicationName}");
    }

    /// <summary>Returns the base address configured for an application, or <see langword="null"/>.</summary>
    public static string? BaseUrl(IConfiguration configuration, string applicationName)
        => configuration[$"{SectionPath}:{applicationName}:BaseUrl"];

    /// <summary>
    /// The one application-setting precedence: an address a started piece published through
    /// <see cref="ProtoInfrastructureSettings"/> wins over static configuration. Readiness, the HTTP
    /// and gRPC client initializers, browser sessions and device clients all resolve through here.
    /// </summary>
    internal static string? ResolveSetting(
        IConfiguration configuration,
        ProtoInfrastructureSettings? settings,
        string key)
    {
        if (settings is not null
            && settings.Values.TryGetValue(key, out var published)
            && !string.IsNullOrWhiteSpace(published))
        {
            return published;
        }

        return configuration[key];
    }

    internal static string SettingsKey(string applicationName, string settingPath)
        => $"{SectionPath}:{applicationName}:{settingPath}";

    /// <summary>
    /// Returns the base address of an application for a running test: an address a started instance
    /// advertised through infrastructure settings wins over static configuration.
    /// </summary>
    public static string? BaseUrl(ProtoExecutionContext context, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return ResolveSetting(
            context.Configuration,
            context.TryService<ProtoInfrastructureSettings>(),
            SettingsKey(applicationName, "BaseUrl"));
    }

    /// <summary>
    /// Returns an application's gRPC address for a running test, with the same precedence as
    /// <see cref="BaseUrl(ProtoExecutionContext, string)"/>: a published value wins over
    /// <c>ProtoTest:Applications:{applicationName}:Grpc:Address</c> in static configuration. Returns
    /// <see langword="null"/> when neither is set, so the caller can fall back to the base address.
    /// </summary>
    public static string? GrpcAddress(ProtoExecutionContext context, string applicationName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        return ResolveSetting(
            context.Configuration,
            context.TryService<ProtoInfrastructureSettings>(),
            SettingsKey(applicationName, "Grpc:Address"));
    }

    /// <summary>
    /// Returns the address of an application endpoint: the application's base URL joined with its
    /// <c>Endpoints:{endpointName}</c> path when one is configured, or the base URL unchanged.
    /// </summary>
    public static string? EndpointAddress(ProtoExecutionContext context, string applicationName, string? endpointName)
    {
        var baseUrl = BaseUrl(context, applicationName);
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(endpointName))
        {
            return baseUrl;
        }

        var path = Endpoint(context.Configuration, applicationName, endpointName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return baseUrl;
        }

        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin)
            ? new Uri(origin, path).ToString()
            : baseUrl;
    }

    /// <summary>Returns the relative path configured for a named endpoint of an application, or null.</summary>
    public static string? Endpoint(IConfiguration configuration, string applicationName, string endpointName)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        return configuration[$"{SectionPath}:{applicationName}:Endpoints:{endpointName}"];
    }

    /// <summary>Returns the error message used when an application has no configured document.</summary>
    public static string MissingSettingMessage(string applicationName, string setting)
        => $"Application '{applicationName}' has no '{setting}' configured. " +
           $"Set 'ProtoTest:Applications:{applicationName}:{setting}'.";
}
