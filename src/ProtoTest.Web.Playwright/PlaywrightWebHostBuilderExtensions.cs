namespace ProtoTest.Web;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Web.Playwright;
using System.Runtime.CompilerServices;

/// <summary>
/// Playwright registration for <see cref="IProtoHostBuilder"/>. Reference the ProtoTest.Web.Playwright
/// package and call <c>AddWeb(...)</c>; the backend is implied by the referenced package. Sessions are
/// created per test via <c>Proto.Context.Web(name)</c>.
/// </summary>
public static class PlaywrightWebHostBuilderExtensions
{
    // First registration supplies the launch defaults and the backend, mirroring the backend factory's
    // first-wins rule; every call still runs its configure callback. The marker is set only after the
    // callback succeeded, so a throwing configure does not poison a later call.
    private static readonly ConditionalWeakTable<IProtoHostBuilder, object> HostDefaults = new();
    private static readonly ConditionalWeakTable<IProtoApplicationBuilder, object> ApplicationDefaults = new();
    private static readonly object Registration = new();

    /// <summary>Adds a Playwright-backed web host.</summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="configure">Optional callback to configure Playwright options for every session.</param>
    public static IProtoHostBuilder AddWeb(
        this IProtoHostBuilder builder,
        Action<PlaywrightWebOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var defaults = PlaywrightWebDefaults.Create(configure);
        // Test-scoped so a browser process is shared by the sessions of one test and disposed with it.
        builder.ConfigureServices(services => services.TryAddScoped<PlaywrightBrowserPool>());
        if (HostDefaults.TryAdd(builder, Registration))
        {
            // Code-configured launch options feed the code-driven default section, so a skip condition can
            // see them; it is inserted first, so real configuration and session sections still win.
            builder.ConfigureAppConfiguration(configuration =>
                configuration.Sources.Insert(0, PlaywrightWebDefaults.Source(defaults)));
        }

        return builder
            .AddCapability(new ProtoCapabilityDescriptor(
                "Playwright", ProtoCapabilityKinds.Browser, "ProtoTest.Web.Playwright"))
            .AddWebBackend(new PlaywrightWebBackendFactory(configure));
    }

    /// <summary>Exposes web under an application. The application's sessions share this backend.</summary>
    public static IProtoApplicationBuilder AddWeb(
        this IProtoApplicationBuilder application,
        Action<PlaywrightWebOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        var defaults = PlaywrightWebDefaults.Create(configure);
        // The application builder cannot reach the host's configuration sources, so the same defaults
        // are injected into the host's configuration when the run starts.
        application.Services.TryAddScoped<PlaywrightBrowserPool>();
        application.Services.AddWebBackend(new PlaywrightWebBackendFactory(configure));
        if (ApplicationDefaults.TryAdd(application, Registration))
        {
            application.Services.AddSingleton<IProtoRunHook>(services =>
                new PlaywrightDefaultsRunHook(
                    services.GetRequiredService<IConfiguration>(),
                    defaults));
            application.RegisterClient("Web", "Default");
        }

        return application.AddCapability(new ProtoCapabilityDescriptor(
            "Playwright", ProtoCapabilityKinds.Browser, "ProtoTest.Web.Playwright"));
    }
}
