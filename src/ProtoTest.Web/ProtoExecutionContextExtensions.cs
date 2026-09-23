namespace ProtoTest.Web;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;
using ProtoTest.Web.Internal;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Returns the web session for this test, creating it on first use. Inside an <c>[Application]</c>
    /// the session targets that application unless <paramref name="application"/> overrides it; its
    /// address comes from the application's <c>BaseUrl</c> (optionally rooted at a named
    /// <paramref name="endpoint"/>). <paramref name="discoverRoutes"/> enables Vue Router route discovery.
    /// </summary>
    public static WebSession Web(
        this ProtoExecutionContext context,
        string? sessionName = null,
        string? application = null,
        string? endpoint = null,
        bool discoverRoutes = false)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolvedName = string.IsNullOrWhiteSpace(sessionName)
            ? ProtoApplicationResolution.ResolveClientName(context, "Web", fallback: "Default")
            : sessionName;
        var resolvedApplication = application ?? ProtoApplicationResolution.ResolveApplicationName(context);

        _ = context.TryService<IWebBackendFactory>()
            ?? throw new InvalidOperationException(
                "No web backend is registered. Reference ProtoTest.Web.Playwright or ProtoTest.Web.Selenium and call AddWeb().");
        if (context.TryClient<WebSession>(resolvedName) is { } existing)
        {
            return existing;
        }

        // Sessions are dynamic clients: their names are not declared at registration, so the accessor
        // creates one on first use and registers it like any other client (completion and disposal run
        // through the client registry).
        var session = new WebSession(
            context, ResolveFactory(context), resolvedName, resolvedApplication, endpoint, discoverRoutes);
        context.RegisterClient(session, resolvedName);
        return session;
    }

    private static IWebBackendFactory ResolveFactory(ProtoExecutionContext context)
    {
        var factories = context.Services.GetServices<IWebBackendFactory>().ToArray();
        return factories.Length switch
        {
            1 => factories[0],
            0 => throw new InvalidOperationException(
                "No web backend is registered. Call AddWeb() from a referenced web backend package."),
            _ => throw new InvalidOperationException(
                $"This host registered several web backends ({string.Join(", ", factories.Select(factory => factory.Name))}); " +
                "a host runs one web backend, so call AddWeb() from the package you want and drop the other.")
        };
    }
}
