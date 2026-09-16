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
        var options = new PlaywrightWebOptions();
        configure?.Invoke(options);
        builder.ConfigureServices(services => services.TryAddScoped<PlaywrightBrowserPool>());
        return builder.AddWebBackend(new PlaywrightWebBackendFactory(options, name), name);
    }
}
