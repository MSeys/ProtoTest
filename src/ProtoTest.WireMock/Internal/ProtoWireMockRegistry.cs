namespace ProtoTest.WireMock.Internal;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

/// <summary>
/// The suite's fake registrations and shared per-run servers. One instance per host: the hook and the
/// context accessor resolve it from the test's services, and <c>AddWireMock</c> fills it while building.
/// </summary>
internal sealed class ProtoWireMockRegistry
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<string, ProtoWireMockSettings> _registrations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ProtoWireMockSession> _runSessions = new(StringComparer.Ordinal);

    /// <summary>Finds the registry in the collection, adding it when this is the first fake.</summary>
    public static ProtoWireMockRegistry GetOrAdd(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services
            .LastOrDefault(descriptor => descriptor.ServiceType == typeof(ProtoWireMockRegistry))
            ?.ImplementationInstance as ProtoWireMockRegistry;
        if (existing is not null)
        {
            return existing;
        }

        var registry = new ProtoWireMockRegistry();
        services.AddSingleton(registry);
        return registry;
    }

    /// <summary>
    /// Registers one fake, reporting whether this call created the registration. A repeated name with
    /// equal settings composes; a repeated name with different settings throws naming the fake instead
    /// of silently keeping the first.
    /// </summary>
    public bool AddOrThrow(string name, ProtoWireMockSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            if (_registrations.TryGetValue(name, out var registered))
            {
                if (registered == settings)
                {
                    return false;
                }

                throw new InvalidOperationException(
                    $"A WireMock fake named '{name}' is already registered " +
                    $"({Describe(registered)}); register a different name instead of '{name}' " +
                    $"({Describe(settings)}).");
            }

            _registrations.Add(name, settings);
            return true;
        }
    }

    /// <summary>Resolves one fake's settings, naming <c>AddWireMock</c> and the known fakes when absent.</summary>
    public ProtoWireMockSettings Resolve(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        lock (_gate)
        {
            if (_registrations.TryGetValue(name, out var settings))
            {
                return settings;
            }

            var known = _registrations.Count == 0
                ? "no WireMock fakes are registered"
                : $"known fakes: {string.Join(", ", _registrations.Keys.Select(key => $"'{key}'"))}";
            throw new InvalidOperationException(
                $"No WireMock fake named '{name}' is registered ({known}). " +
                "Call AddWireMock on the host builder to register it.");
        }
    }

    /// <summary>Gets the registered fake names, for the default resolution and the unknown-name error.</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            lock (_gate)
            {
                return [.. _registrations.Keys];
            }
        }
    }

    /// <summary>
    /// Gets the shared server for a per-run fake, starting it on first use. One server per fake: the
    /// run owns it and releases it with the run, while every test that uses the fake resets its stubs.
    /// </summary>
    public ProtoWireMockSession GetOrStartRunSession(string name)
    {
        var settings = Resolve(name);
        if (!settings.PerRun)
        {
            throw new InvalidOperationException($"WireMock fake '{name}' is not a per-run fake.");
        }

        lock (_gate)
        {
            if (_runSessions.TryGetValue(name, out var session))
            {
                return session;
            }

            session = new ProtoWireMockSession(name, settings, ProtoTest.Core.ProtoResourceScope.Run);
            _runSessions.Add(name, session);
            return session;
        }
    }

    /// <summary>Removes the shared server without stopping it; the caller stops what it removed.</summary>
    public bool TryRemoveRunSession(string name, out ProtoWireMockSession? session)
    {
        lock (_gate)
        {
            if (_runSessions.TryGetValue(name, out session))
            {
                _runSessions.Remove(name);
                return true;
            }

            session = null;
            return false;
        }
    }

    private static string Describe(ProtoWireMockSettings settings)
    {
        var lifetime = settings.PerRun ? "per-run" : "per-test";
        return settings.Port is null ? lifetime : $"{lifetime} on port {settings.Port}";
    }
}
