namespace ProtoTest.Core.Internal;

using System.Diagnostics;

internal static class ProtoHostRegistry
{
    private static readonly ProtoLock Gate = new();
    private static readonly HashSet<ProtoHost> ActiveHosts = [];

    public static void Register(ProtoHost host)
    {
        lock (Gate)
        {
            ActiveHosts.Add(host);
        }
    }

    public static void Unregister(ProtoHost host)
    {
        lock (Gate)
        {
            ActiveHosts.Remove(host);
        }
    }

    public static ProtoHost GetCurrent(ProtoHost? contextualHost)
    {
        if (contextualHost is not null)
        {
            return contextualHost;
        }

        lock (Gate)
        {
            return ActiveHosts.Count switch
            {
                1 => ActiveHosts.Single(),
                0 => throw new InvalidOperationException("No active ProtoHost is available."),
                _ => throw new InvalidOperationException(
                    "Multiple ProtoHost instances are active. Proto.Host is only available inside a test context.")
            };
        }
    }

    /// <summary>
    /// Finds the test trace that an application span belongs to, searching every active host. Telemetry
    /// callbacks run outside the test's flow and may see multiple hosts, but a W3C trace id is unique,
    /// so the search is unambiguous.
    /// </summary>
    public static IProtoTraceWriter? FindTraceWriter(ActivityTraceId traceId)
    {
        ProtoHost[] hosts;
        lock (Gate)
        {
            hosts = ActiveHosts.ToArray();
        }

        foreach (var host in hosts)
        {
            if (host.Trace.FindWriter(traceId) is { } writer)
            {
                return writer;
            }
        }

        return null;
    }
}
