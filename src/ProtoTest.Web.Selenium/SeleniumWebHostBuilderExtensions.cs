namespace ProtoTest.Web;

using OpenQA.Selenium;
using ProtoTest.Core;
using ProtoTest.Web.Selenium;
using System.Runtime.CompilerServices;

/// <summary>
/// Selenium registration for <see cref="IProtoHostBuilder"/>. Reference the ProtoTest.Web.Selenium
/// package and call <c>AddWeb(...)</c>; the backend is implied by the referenced package. Sessions are
/// created per test via <c>Proto.Context.Web(name)</c>.
/// </summary>
public static class SeleniumWebHostBuilderExtensions
{
    // The host overload is already first-wins through AddWebBackend; the application overload also
    // registers the application's default client, so it guards the whole repeated call.
    private static readonly ConditionalWeakTable<IProtoApplicationBuilder, object> RegisteredApplications = new();
    private static readonly object Registration = new();

    /// <summary>Adds a Selenium-backed web host.</summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="createDriver">Creates the WebDriver for each session.</param>
    /// <param name="configure">Optional callback to configure Selenium options for every session.</param>
    public static IProtoHostBuilder AddWeb(
        this IProtoHostBuilder builder,
        Func<IWebDriver> createDriver,
        Action<SeleniumWebOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(createDriver);
        return builder
            .AddCapability(new ProtoCapabilityDescriptor(
                "Selenium", ProtoCapabilityKinds.Browser, "ProtoTest.Web.Selenium"))
            .AddWebBackend(new SeleniumWebBackendFactory(createDriver, configure));
    }

    /// <summary>Exposes web under an application. The application's sessions share this backend.</summary>
    public static IProtoApplicationBuilder AddWeb(
        this IProtoApplicationBuilder application,
        Func<IWebDriver> createDriver,
        Action<SeleniumWebOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(createDriver);
        if (!RegisteredApplications.TryAdd(application, Registration))
        {
            return application;
        }

        application.Services.AddWebBackend(new SeleniumWebBackendFactory(createDriver, configure));
        application.RegisterClient("Web", "Default");
        return application.AddCapability(new ProtoCapabilityDescriptor(
            "Selenium", ProtoCapabilityKinds.Browser, "ProtoTest.Web.Selenium"));
    }
}
