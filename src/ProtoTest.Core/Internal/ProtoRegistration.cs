namespace ProtoTest.Core;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The one "first registration wins, later calls are no-ops" primitive: find a matching registration
/// with the caller's identity rule, otherwise add the new one and report that this call won. The public
/// guard, the capability declarations and the collector registrations all read through it, so the
/// once-only rule exists in one place.
/// </summary>
internal static class ProtoRegistration
{
    /// <summary>
    /// Adds <paramref name="registration"/> when no existing descriptor matches; returns whether this
    /// call added it. <paramref name="matches"/> is the caller's identity rule.
    /// </summary>
    public static bool TryAdd(
        IServiceCollection services,
        Func<ServiceDescriptor, bool> matches,
        ServiceDescriptor registration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(registration);
        if (services.Any(matches))
        {
            return false;
        }

        services.Add(registration);
        return true;
    }

    /// <summary>Adds a marker singleton when no registered instance matches it.</summary>
    public static bool TryAdd<TMarker>(IServiceCollection services, TMarker marker, Func<TMarker, bool> matches)
        where TMarker : class
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(matches);
        return TryAdd(
            services,
            descriptor => descriptor.ImplementationInstance is TMarker existing && matches(existing),
            ServiceDescriptor.Singleton(marker));
    }

    /// <summary>Registers <typeparamref name="TMarker"/> once; its presence in the collection is the marker.</summary>
    public static bool TryRegisterOnce<TMarker>(IServiceCollection services) where TMarker : class
    {
        ArgumentNullException.ThrowIfNull(services);
        return TryAdd(
            services,
            descriptor => descriptor.ServiceType == typeof(TMarker),
            ServiceDescriptor.Singleton<TMarker, TMarker>());
    }

    /// <summary>The same rule for a builder whose marker lives in a weak table instead of the collection.</summary>
    public static bool TryAdd<TBuilder>(ConditionalWeakTable<TBuilder, object> registrations, TBuilder builder)
        where TBuilder : class
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(builder);
        return registrations.TryAdd(builder, Marker);
    }

    /// <summary>Returns the registered instance matching <paramref name="matches"/>, or <see langword="null"/>.</summary>
    public static TMarker? Find<TMarker>(IServiceCollection services, Func<TMarker, bool> matches)
        where TMarker : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(matches);
        foreach (var descriptor in services)
        {
            if (descriptor.ImplementationInstance is TMarker existing && matches(existing))
            {
                return existing;
            }
        }

        return null;
    }

    private static readonly object Marker = new();
}
