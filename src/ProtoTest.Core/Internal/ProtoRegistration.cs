namespace ProtoTest.Core;

using Microsoft.Extensions.DependencyInjection;

/// <summary>Registers a marker once, with the first matching registration taking precedence.</summary>
internal static class ProtoRegistration
{
    public static bool TryAdd<TMarker>(
        IServiceCollection services,
        TMarker marker,
        Func<TMarker, bool> matches)
        where TMarker : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(matches);

        if (services.Any(descriptor =>
            descriptor.ImplementationInstance is TMarker existing && matches(existing)))
        {
            return false;
        }

        services.AddSingleton(marker);
        return true;
    }
}
