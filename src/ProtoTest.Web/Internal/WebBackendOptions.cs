namespace ProtoTest.Web.Internal;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// The backend options bootstrap every factory shares: create the options, apply the code callback, then
/// bind configuration once - started infrastructure overrides it, the backend's own section
/// (<c>ProtoTest:Web:{backend}</c>) overrides the code callback, and
/// <c>ProtoTest:Web:Sessions:{session}</c> overrides both - and validate the bound result once.
/// </summary>
internal static class WebBackendOptions
{
    internal static TOptions Resolve<TOptions>(
        ProtoExecutionContext context,
        string sessionName,
        Action<TOptions>? configure = null,
        Action<TOptions>? validate = null)
        where TOptions : class, IProtoConfigurableOptions, new()
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);

        var options = new TOptions();
        configure?.Invoke(options);

        var configuration = WithInfrastructureSettings(
            context.Configuration, context.TryService<ProtoInfrastructureSettings>());
        options.BindFromConfiguration(configuration);
        configuration.GetSection($"ProtoTest:Web:Sessions:{sessionName}").Bind(options);
        validate?.Invoke(options);
        return options;
    }

    private static IConfiguration WithInfrastructureSettings(
        IConfiguration configuration,
        ProtoInfrastructureSettings? infrastructureSettings)
    {
        var values = infrastructureSettings?.Values;
        if (values is null || values.Count == 0)
        {
            return configuration;
        }

        return new ConfigurationBuilder()
            .AddConfiguration(configuration)
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();
    }
}
