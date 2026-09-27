namespace ProtoTest.Aspire;

using ProtoTest.Aspire.Internal;
using ProtoTest.Core;

/// <summary>Reaches the Aspire AppHost a run started from inside a test.</summary>
public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Gets the base address the AppHost published for the resource: the value of
    /// <c>ProtoTest:Applications:{application}:BaseUrl</c> the test's settings resolve, so a
    /// resource renamed with <c>MapResource</c> is found under its application name.
    /// </summary>
    public static string AspireResource(this ProtoExecutionContext context, string resource)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var registry = context.TryService<ProtoAspireRegistry>()
            ?? throw new InvalidOperationException(
                "This host was not built with ProtoTest.Aspire; register an AppHost with builder.AddAspireAppHost<TEntryPoint>().");

        var application = registry.ApplicationFor(resource);
        return ProtoApplication.BaseUrl(context, application)
            ?? throw new InvalidOperationException(
                $"Aspire resource '{resource}' has no address yet. The AppHost publishes 'ProtoTest:Applications:{application}:BaseUrl' when the run starts; " +
                "configure that key to point the suite at a deployed topology instead.");
    }
}
