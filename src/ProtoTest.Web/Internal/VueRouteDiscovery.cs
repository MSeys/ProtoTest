namespace ProtoTest.Web.Internal;

using System.Reflection;

/// <summary>
/// The Vue Router discovery script, embedded as a JavaScript resource so it reads as JavaScript and the
/// C# side holds only the contract around it. The script answers with a JSON array of route paths, or
/// <see langword="null"/> when there is no Vue application or router - a missing framework is an
/// answer, not a failure.
/// </summary>
internal static class VueRouteDiscovery
{
    private const string ResourceName = "ProtoTest.Web.Internal.vue-route-discovery.js";

    /// <summary>The script evaluated on the page after a navigation.</summary>
    public static string Script { get; } = ReadScript();

    private static string ReadScript()
    {
        using var stream = typeof(VueRouteDiscovery).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded Vue route discovery script '{ResourceName}' is missing from the assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
