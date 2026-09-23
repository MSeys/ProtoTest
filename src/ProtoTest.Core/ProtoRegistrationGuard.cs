namespace ProtoTest.Core;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// One implementation of the "first registration wins, later calls are no-ops" rule every integration
/// repeats, for both a service collection and a builder that keeps its marker out of DI.
/// </summary>
public static class ProtoRegistrationGuard
{
    /// <summary>
    /// Registers <typeparamref name="TMarker"/> once and reports whether this call won. The marker is
    /// the integration's own type, so its presence means exactly "this integration registered".
    /// </summary>
    public static bool TryRegisterOnce<TMarker>(IServiceCollection services) where TMarker : class
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(TMarker)))
        {
            return false;
        }

        services.AddSingleton<TMarker>();
        return true;
    }

    /// <summary>The same rule for a builder whose marker lives in a weak table instead of the collection.</summary>
    public static bool TryRegisterOnce<TBuilder>(
        ConditionalWeakTable<TBuilder, object> registrations,
        TBuilder builder)
        where TBuilder : class
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(builder);
        return registrations.TryAdd(builder, Marker);
    }

    private static readonly object Marker = new();
}
