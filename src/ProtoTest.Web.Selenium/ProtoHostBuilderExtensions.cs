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
        var options = new SeleniumWebOptions();
        configure?.Invoke(options);
        if (options.ActionTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.ActionTimeout));
        if (options.PollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.PollInterval));
        return builder.AddWebBackend(new SeleniumWebBackendFactory(createDriver, options, name), name);
    }
}
