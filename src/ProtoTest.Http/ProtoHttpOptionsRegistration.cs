namespace ProtoTest.Http;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// Registers per-protocol response and attachment options under a stable protocol key, so REST and
/// GraphQL keep their own configuration sections even though they share the base option types.
/// </summary>
public static class ProtoHttpOptionsRegistration
{
    /// <summary>
    /// Registers a protocol's response options under its key, binding from
    /// <paramref name="configurationSectionName"/> when first resolved. A second registration for the
    /// same key is ignored.
    /// </summary>
    public static void TryAddResponseOptions(
        IServiceCollection services,
        string protocolName,
        string configurationSectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSectionName);
        ProtoOptionsRegistration.ConfigureKeyed(
            services,
            protocolName,
            () => new ProtoHttpResponseOptions(configurationSectionName));
    }

    /// <summary>
    /// Registers a protocol's response options with the supplied code configuration. Every call's
    /// callback is applied in registration order before the known configuration section is bound over
    /// the result, so repeated calls compose instead of replacing each other.
    /// </summary>
    public static void ConfigureResponseOptions(
        IServiceCollection services,
        string protocolName,
        string configurationSectionName,
        Action<ProtoHttpResponseOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSectionName);
        ProtoOptionsRegistration.ConfigureKeyed(
            services,
            protocolName,
            () => new ProtoHttpResponseOptions(configurationSectionName),
            configure);
    }

    /// <summary>
    /// Registers a protocol's attachment options with the supplied code configuration. Every call's
    /// callback is applied in registration order before the known configuration section is bound over
    /// the result, so repeated calls compose instead of replacing each other.
    /// </summary>
    public static void ConfigureAttachmentOptions(
        IServiceCollection services,
        string protocolName,
        string configurationSectionName,
        Action<ProtoHttpAttachmentOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSectionName);
        ProtoOptionsRegistration.ConfigureKeyed(
            services,
            protocolName,
            () => new ProtoHttpAttachmentOptions(configurationSectionName),
            configure);
    }
}
