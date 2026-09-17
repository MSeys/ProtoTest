namespace ProtoTest.Web.Playwright;

using ProtoTest.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

public static class ProtoHostBuilderExtensions
{
    public static IProtoHostBuilder AddPlaywrightWeb(
        this IProtoHostBuilder builder,
        Action<PlaywrightWebOptions>? configure = null,
        string name = "Default")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var options = new PlaywrightWebOptions();
        configure?.Invoke(options);
        var binder = new WebBackendOptionsBinder<PlaywrightWebOptions>(options, PlaywrightWebOptions.BackendName, name);
        builder.ConfigureServices(services => services.TryAddScoped<PlaywrightBrowserPool>());
        return builder.AddWebBackend(new PlaywrightWebBackendFactory(binder, name), name);
    }
}
