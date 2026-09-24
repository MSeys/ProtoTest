namespace ProtoTest.Web;

using System.Runtime.CompilerServices;
using OpenQA.Selenium;
using ProtoTest.Core;
using ProtoTest.Web.Selenium;

/// <summary>
/// Selenium registration for <see cref="IProtoHostBuilder"/>. Reference the ProtoTest.Web.Selenium
/// package and call <c>AddWeb(...)</c>; the backend is implied by the referenced package. Sessions are
/// created per test via <c>Proto.Context.Web(name)</c>. The driver a session creates is owned by
/// ProtoTest: it is quit and disposed when the session completes, so the factory must return a fresh
/// driver rather than a shared one.
/// </summary>
public static class SeleniumWebHostBuilderExtensions
{
    // The host overload is already first-wins through AddWebBackend; the application overload also
    // registers the application's default client, so it guards the whole repeated call.
    private static readonly ConditionalWeakTable<IProtoApplicationBuilder, object> RegisteredApplications = new();

    /// <summary>Adds a Selenium-backed web host.</summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="createDriver">
    /// Creates a fresh WebDriver for each session. ProtoTest owns it: the session quits and disposes it,
    /// so returning a shared driver would tear it down under later tests.
    /// </param>
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
                "Selenium", ProtoCapabilityKinds.Browser, SeleniumWebBackend.TraceSource))
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
        if (!ProtoRegistrationGuard.TryRegisterOnce(RegisteredApplications, application))
        {
            return application;
        }

        application.Services.AddWebBackend(new SeleniumWebBackendFactory(createDriver, configure));
        application.RegisterClient("Web", "Default");
        return application.AddCapability(new ProtoCapabilityDescriptor(
            "Selenium", ProtoCapabilityKinds.Browser, SeleniumWebBackend.TraceSource));
    }
}
