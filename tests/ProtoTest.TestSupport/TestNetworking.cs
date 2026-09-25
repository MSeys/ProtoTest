namespace ProtoTest.TestSupport;

using System.Net;
using System.Net.Sockets;

/// <summary>The network helpers suites share instead of copying (audit TST-2).</summary>
public static class TestNetworking
{
    /// <summary>
    /// Reserves an ephemeral loopback TCP port and releases it, for tests that need an address with
    /// nothing behind it - a dead published address, a connect-failure target, a ready-on port.
    /// </summary>
    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }
}
