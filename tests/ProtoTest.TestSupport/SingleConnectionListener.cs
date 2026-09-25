namespace ProtoTest.TestSupport;

using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// A loopback HTTP listener on a free port that holds exactly one request until the test answers it.
/// The <see cref="RequestReceived"/> signal is what a readiness or client test waits on, so it proves
/// the address was actually reached without a sleep or a fixed timing window (audit TST-2).
/// </summary>
public sealed class SingleConnectionListener : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly TaskCompletionSource _requestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _serve;

    private SingleConnectionListener(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _serve = ServeAsync();
    }

    /// <summary>The port the listener owns; compose it into the address the host should probe.</summary>
    public int Port { get; }

    /// <summary>The loopback address of this listener.</summary>
    public string Address => $"http://127.0.0.1:{Port}";

    /// <summary>Completes when a client sent a request and the listener is holding it for an answer.</summary>
    public Task RequestReceived => _requestReceived.Task;

    /// <summary>Completes when the held request has been answered.</summary>
    public Task Completed => _serve;

    /// <summary>Starts a listener on an ephemeral loopback port.</summary>
    public static SingleConnectionListener Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new SingleConnectionListener(listener);
    }

    /// <summary>Answers the held request with a minimal <c>200 OK</c>; a no-op when nothing is held.</summary>
    public void Answer() => _answer.TrySetResult();

    private async Task ServeAsync()
    {
        using var client = await _listener.AcceptTcpClientAsync();
        // Read the request before answering: writing first and closing makes Windows abort the
        // connection while the client is still sending, and the probe rightly reports "not ready".
        var request = new byte[4096];
        await client.GetStream().ReadAsync(request);
        _requestReceived.TrySetResult();
        await _answer.Task;
        var response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok");
        await client.GetStream().WriteAsync(response);
        await client.GetStream().FlushAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Teardown may run before a request arrives; unblock a held one first so it completes normally.
        _answer.TrySetResult();
        _listener.Stop();
        try
        {
            await _serve;
        }
        catch (SocketException)
        {
            // The test's teardown stopped the listener before a client connected.
        }
        catch (ObjectDisposedException)
        {
            // Same, for the pending accept the stop disposed out from under.
        }
    }
}
