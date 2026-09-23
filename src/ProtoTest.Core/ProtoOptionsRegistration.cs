namespace ProtoTest.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// Registers a single instance of an options type and applies every configuration callback in
/// registration order before binding the type's known configuration section over the result, so
/// repeated registration composes instead of replacing earlier callbacks.
/// </summary>
public static class ProtoOptionsRegistration
{
    /// <summary>
    /// Registers <typeparamref name="TOptions"/> once in <paramref name="services"/>. Every call's
    /// <paramref name="configure"/> callback runs in registration order, then the section named by
    /// <see cref="IProtoConfigurableOptions.ConfigurationSectionName"/> binds over the result.
    /// </summary>
    public static void Configure<TOptions>(
        IServiceCollection services,
        Func<TOptions> factory,
        Action<TOptions>? configure = null)
        where TOptions : class, IProtoConfigurableOptions
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);

        if (configure is not null)
        {
            services.AddSingleton(new ConfigureCallback<TOptions>(configure));
        }

        services.TryAddSingleton(serviceProvider =>
        {
            var options = factory();
            foreach (var callback in serviceProvider.GetServices<ConfigureCallback<TOptions>>())
            {
                callback.Callback(options);
            }

            options.BindFromConfiguration(serviceProvider.GetRequiredService<IConfiguration>());
            return options;
        });
    }

    private sealed record ConfigureCallback<TOptions>(Action<TOptions> Callback);

    /// <summary>
    /// Builds an options instance for a service provider: the factory runs, then the code callback, and
    /// the type's known configuration section binds over the result - configuration wins over code.
    /// </summary>
    public static TOptions Resolve<TOptions>(
        IServiceProvider services,
        Func<TOptions> factory,
        Action<TOptions>? configure = null)
        where TOptions : class, IProtoConfigurableOptions
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        var options = factory();
        configure?.Invoke(options);
        options.BindFromConfiguration(services.GetRequiredService<IConfiguration>());
        return options;
    }

    /// <summary>The <see cref="Resolve{TOptions}(IServiceProvider, Func{TOptions}, Action{TOptions})"/>
    /// form for an options type with a parameterless constructor.</summary>
    public static TOptions Resolve<TOptions>(IServiceProvider services, Action<TOptions>? configure = null)
        where TOptions : class, IProtoConfigurableOptions, new()
        => Resolve(services, () => new TOptions(), configure);
}
