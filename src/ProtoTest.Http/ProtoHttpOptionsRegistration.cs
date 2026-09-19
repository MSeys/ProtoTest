namespace ProtoTest.Http;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.TryAddKeyedSingleton<ProtoHttpResponseOptions>(protocolName, (serviceProvider, _) =>
        {
            var options = new ProtoHttpResponseOptions(configurationSectionName);
            ApplyCodeConfiguration(serviceProvider, protocolName, options);
            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return options;
        });
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
        if (configure is not null)
            services.AddKeyedSingleton(protocolName, new ConfigureCallback<ProtoHttpResponseOptions>(configure));
        TryAddResponseOptions(services, protocolName, configurationSectionName);
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
        if (configure is not null)
            services.AddKeyedSingleton(protocolName, new ConfigureCallback<ProtoHttpAttachmentOptions>(configure));
        TryAddAttachmentOptions(services, protocolName, configurationSectionName);
    }

    private static void TryAddAttachmentOptions(
        IServiceCollection services,
        string protocolName,
        string configurationSectionName)
    {
        services.TryAddKeyedSingleton<ProtoHttpAttachmentOptions>(protocolName, (serviceProvider, _) =>
        {
            var options = new ProtoHttpAttachmentOptions(configurationSectionName);
            ApplyCodeConfiguration(serviceProvider, protocolName, options);
            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return options;
        });
    }

    private static void ApplyCodeConfiguration<TOptions>(
        IServiceProvider serviceProvider,
        string protocolName,
        TOptions options)
    {
        foreach (var callback in serviceProvider.GetKeyedServices<ConfigureCallback<TOptions>>(protocolName))
            callback.Callback(options);
    }

    private sealed record ConfigureCallback<TOptions>(Action<TOptions> Callback);
}
