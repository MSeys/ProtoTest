namespace ProtoTest.Web;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;

/// <summary>
/// The backend options bootstrap every factory shares: create the options, apply the code callback, then
/// bind configuration once - started infrastructure overrides it, and the backend's own section
/// (<c>ProtoTest:Web:{backend}</c>) overrides the code callback - and validate the bound result once.
/// A hand-written backend's factory calls this so it resolves options exactly like the shipped ones.
/// </summary>
public static class WebBackendOptions
{
    /// <summary>Creates, configures, binds and validates the backend's options for one test.</summary>
    public static TOptions Resolve<TOptions>(
        ProtoExecutionContext context,
        Action<TOptions>? configure = null,
        Action<TOptions>? validate = null)
        where TOptions : class, IProtoConfigurableOptions, new()
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = new TOptions();
        configure?.Invoke(options);

        var configuration = WithInfrastructureSettings(
            context.Configuration, context.TryService<ProtoInfrastructureSettings>());
        options.BindFromConfiguration(configuration);
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
