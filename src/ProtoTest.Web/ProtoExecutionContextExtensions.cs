namespace ProtoTest.Web;

using ProtoTest.Web.Internal;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Returns the web session for this test, creating it on first use. Inside an <c>[Application]</c>
    /// the session targets that application unless <paramref name="application"/> overrides it, and the
    /// default session name comes from the application's <c>Web:…</c> binding.
    /// </summary>
    public static WebSession Web(
        this ProtoExecutionContext context,
        string? sessionName = null,
        string? application = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolvedName = string.IsNullOrWhiteSpace(sessionName)
            ? ProtoApplicationResolution.ResolveClientName(context, "Web", fallback: "Default")
            : sessionName;
        var resolvedApplication = application ?? ProtoApplicationResolution.ResolveApplicationName(context);

        var registry = context.TryService<WebSessionRegistry>()
            ?? throw new InvalidOperationException(
                "No web backend is registered. Reference ProtoTest.Web.Playwright or ProtoTest.Web.Selenium and call AddWeb().");
        if (registry.TryGet(resolvedName, out var existing))
        {
            return existing;
        }

        var session = new WebSession(context, ResolveFactory(context), resolvedName, resolvedApplication);
        context.RegisterClient(session, resolvedName);
        registry.Add(session);
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
                "Multiple web backends are registered; a host supports a single web backend.")
        };
    }
}
