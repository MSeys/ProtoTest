namespace ProtoTest.Core;

internal static class ProtoHostRegistry
{
    private static readonly object Gate = new();
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
}
