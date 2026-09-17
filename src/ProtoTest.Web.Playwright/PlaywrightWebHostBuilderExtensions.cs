namespace ProtoTest.Web;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoTest.Core;
using ProtoTest.Web.Playwright;

/// <summary>
/// Playwright registration for <see cref="IProtoHostBuilder"/>. Reference the ProtoTest.Web.Playwright
/// package and call <c>AddWeb(...)</c>; the backend is implied by the referenced package. Sessions are
/// created per test via <c>Proto.Context.Web(name)</c>.
/// </summary>
public static class PlaywrightWebHostBuilderExtensions
{
    /// <summary>Adds a Playwright-backed web host.</summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="configure">Optional callback to configure Playwright options for every session.</param>
    public static IProtoHostBuilder AddWeb(
        this IProtoHostBuilder builder,
        Action<PlaywrightWebOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Test-scoped so a browser process is shared by the sessions of one test and disposed with it.
        builder.ConfigureServices(services => services.TryAddScoped<PlaywrightBrowserPool>());
        return builder.AddWebBackend(new PlaywrightWebBackendFactory(configure));
    }

    /// <summary>Exposes web under an application. The application's sessions share this backend.</summary>
    public static IProtoApplicationBuilder AddWeb(
        this IProtoApplicationBuilder application,
        Action<PlaywrightWebOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Services.TryAddScoped<PlaywrightBrowserPool>();
        application.Services.AddWebBackend(new PlaywrightWebBackendFactory(configure));
        application.RegisterClient("Web", "Default");
        return application;
    }
}
