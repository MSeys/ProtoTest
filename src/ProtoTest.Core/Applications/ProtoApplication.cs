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
