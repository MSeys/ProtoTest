namespace ProtoTest.Aspire;

using ProtoTest.Aspire.Internal;
using ProtoTest.Core;

/// <summary>Reaches the Aspire AppHost a run started from inside a test.</summary>
public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Gets the value the AppHost published for the resource: the application's
    /// <c>ProtoTest:Applications:{application}:BaseUrl</c> for an endpoint resource, or the target's
    /// declared key for a resource mapped with <c>MapConnectionString</c>. A resource renamed with
    /// <c>MapResource</c> is found under its application name.
    /// </summary>
    public static string AspireResource(this ProtoExecutionContext context, string resource)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var registry = context.TryService<ProtoAspireRegistry>()
            ?? throw new InvalidOperationException(
                "This host was not built with ProtoTest.Aspire; register an AppHost with builder.AddAspireAppHost<TEntryPoint>().");

        var key = registry.KeyFor(resource);
        return ProtoApplication.ResolveSetting(
                context.Configuration,
                context.TryService<ProtoInfrastructureSettings>(),
                key)
            ?? throw new InvalidOperationException(
                $"Aspire resource '{resource}' has no value yet: the AppHost publishes '{key}' when the run starts. " +
                "Set the key to point the suite at a deployed topology instead, or select the AppHost with " +
                $"'{ProtoAspireOptions.SelectionKey}=true'.");
    }
}
