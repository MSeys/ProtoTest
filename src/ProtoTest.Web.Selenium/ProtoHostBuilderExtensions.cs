namespace ProtoTest.Web.Selenium;

using OpenQA.Selenium;
using ProtoTest.Core;

public static class ProtoHostBuilderExtensions
{
    public static IProtoHostBuilder AddSeleniumWeb(
        this IProtoHostBuilder builder,
        Func<IWebDriver> createDriver,
        Action<SeleniumWebOptions>? configure = null,
        string name = "Default")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(createDriver);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var options = new SeleniumWebOptions();
        configure?.Invoke(options);
        var binder = new WebBackendOptionsBinder<SeleniumWebOptions>(
            options, SeleniumWebOptions.BackendName, name, SeleniumWebOptions.Validate);
        return builder.AddWebBackend(new SeleniumWebBackendFactory(createDriver, binder, name), name);
    }
}
