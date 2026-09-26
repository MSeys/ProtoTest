namespace ProtoTest.Core;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The public façade for the one internal once-only rule (<c>ProtoRegistration</c>): first registration
/// wins, later calls are no-ops. The guard reads through that helper for both a service collection and a
/// builder that keeps its marker out of DI, so an integration's idempotency cannot drift from Core's.
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
        return ProtoRegistration.TryRegisterOnce<TMarker>(services);
    }

    /// <summary>The same rule for a builder whose marker lives in a weak table instead of the collection.</summary>
    public static bool TryRegisterOnce<TBuilder>(
        ConditionalWeakTable<TBuilder, object> registrations,
        TBuilder builder)
        where TBuilder : class
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(builder);
        return ProtoRegistration.TryAdd(registrations, builder);
    }
}
