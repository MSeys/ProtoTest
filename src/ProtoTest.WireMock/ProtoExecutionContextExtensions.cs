namespace ProtoTest.WireMock;

using ProtoTest.Core;
using ProtoTest.WireMock.Internal;

/// <summary>Reaches the suite's fake HTTP services from the running test.</summary>
public static class ProtoExecutionContextExtensions
{
    /// <summary>
    /// Gets the fake's client, starting its server on first use. Without a <paramref name="name"/>
    /// the suite's only fake is used; a name is required when several fakes are registered, and an
    /// unknown name fails naming <c>AddWireMock</c> and the known fakes. A per-test fake starts a
    /// fresh server for this test and stops it with the test; a per-run fake shares the run's server.
    /// </summary>
    public static ProtoWireMockClient WireMock(this ProtoExecutionContext context, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var registry = context.TryService<ProtoWireMockRegistry>() ?? throw new InvalidOperationException(
            "No WireMock fakes are composed for this host. Call AddWireMock on the host builder.");

        var resolved = name ?? DefaultName(registry);
        var existing = context.TryClient<ProtoWireMockClient>(resolved);
        if (existing is not null)
        {
            return existing;
        }

        var settings = registry.Resolve(resolved);
        var session = settings.PerRun
            ? registry.GetOrStartRunSession(resolved)
            : new ProtoWireMockSession(resolved, settings, ProtoResourceScope.Test);
        session.Start(context.Trace, scope: context.TestName);

        if (!settings.PerRun)
        {
            context.RegisterResource(session);
        }

        var client = new ProtoWireMockClient(session, context);
        context.RegisterClient(client, resolved);
        return client;
    }

    private static string DefaultName(ProtoWireMockRegistry registry)
    {
        var names = registry.Names;
        if (names.Count == 1)
        {
            return names[0];
        }

        if (names.Count == 0)
        {
            throw new InvalidOperationException(
                "No WireMock fakes are composed for this host. Call AddWireMock on the host builder.");
        }

        throw new InvalidOperationException(
            $"Several WireMock fakes are registered ({string.Join(", ", names.Select(fake => $"'{fake}'"))}); " +
            "pass the fake's name to WireMock(name).");
    }
}
