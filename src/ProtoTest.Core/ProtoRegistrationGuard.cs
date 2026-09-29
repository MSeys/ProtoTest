namespace ProtoTest.Core;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The public facade for the one internal once-only rule (<c>ProtoRegistration</c>): first registration
/// wins, later calls are no-ops. The guard reads through that helper for a service collection, a marker
/// with the caller's own identity rule, and a builder that keeps its marker out of DI, so an
/// integration's idempotency cannot drift from Core's.
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

    /// <summary>
    /// Registers <paramref name="marker"/> once and reports whether this call won.
    /// <paramref name="matches"/> is the caller's identity rule and decides what "already registered"
    /// means, so an integration whose identity is a value - a target name, a transport - guards repeats
    /// the same way the type-marker form does.
    /// </summary>
    public static bool TryRegisterOnce<TMarker>(
        IServiceCollection services,
        TMarker marker,
        Func<TMarker, bool> matches)
        where TMarker : class
        => ProtoRegistration.TryAdd(services, marker, matches);

    /// <summary>
    /// Returns the registered marker instance matching <paramref name="matches"/>, or
    /// <see langword="null"/>. The read half of the once-only rule: a later caller can observe the
    /// registration that won instead of deriving it again.
    /// </summary>
    public static TMarker? Find<TMarker>(IServiceCollection services, Func<TMarker, bool> matches)
        where TMarker : class
        => ProtoRegistration.Find(services, matches);

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
