namespace ProtoTest.Feedback.Tests;

using System.Net;
using System.Net.Sockets;
using System.Text;

/// <summary>
/// A loopback HTTP endpoint the channel tests post to. It records the request it received and answers
/// with the configured status, so a test proves what actually left the process through the HTTP stack
/// instead of stubbing the client.
/// </summary>
internal sealed class RecordingHttpServer : IDisposable, IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;
    private readonly List<RecordedRequest> _requests = [];
    private readonly object _gate = new();
    private readonly int _status;
    private readonly Func<RecordedRequest, (int Status, string Body)?>? _respond;
    private readonly string _reason;

    private RecordingHttpServer(TcpListener listener, int status, Func<RecordedRequest, (int Status, string Body)?>? respond = null)
    {
        _listener = listener;
        _status = status;
        _respond = respond;
        _reason = status switch
        {
            200 => "OK",
            201 => "Created",
            202 => "Accepted",
            400 => "Bad Request",
            404 => "Not Found",
            500 => "Internal Server Error",
            _ => "Status"
        };
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _serve = ServeAsync();
    }

    public int Port { get; }

    /// <summary>The endpoint URL a target points at.</summary>
    public string Url => $"http://127.0.0.1:{Port}/";

    /// <summary>The requests received so far, in arrival order.</summary>
    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>Starts a listener on an ephemeral loopback port.</summary>
    public static RecordingHttpServer Start(int status = 201)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new RecordingHttpServer(listener, status);
    }

    /// <summary>Starts an endpoint that answers the requests <paramref name="respond"/> picks with their own status and body.</summary>
    public static RecordingHttpServer Start(int status, Func<RecordedRequest, (int Status, string Body)?> respond)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new RecordingHttpServer(listener, status, respond);
    }

    private async Task ServeAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                break;
            }

            try
            {
                await HandleAsync(client, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // A client that disconnected mid-request records nothing; the listener stays up.
            }
            finally
            {
                client.Dispose();
            }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        var request = await ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _requests.Add(request);
        }

        var (status, body) = _respond?.Invoke(request) ?? (_status, "ok");
        var content = Encoding.UTF8.GetBytes(body);
        var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {status} {(status == _status ? _reason : "Reply")}\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n")
            .Concat(content)
            .ToArray();
        await stream.WriteAsync(response, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<RecordedRequest> ReadAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var received = new List<byte>();
        var headerEnd = -1;
        var buffer = new byte[8192];
        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new IOException("The connection closed before the request headers arrived.");
            }

            received.AddRange(buffer.AsSpan(0, read).ToArray());
            headerEnd = HeaderEnd(received);
        }

        var headerText = Encoding.ASCII.GetString(received.ToArray(), 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var startLine = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        var bodyStart = headerEnd + 4;
        var contentLength = headers.TryGetValue("Content-Length", out var length) && int.TryParse(length, out var parsed)
            ? parsed
            : 0;
        while (received.Count - bodyStart < contentLength)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            received.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        var body = Encoding.UTF8.GetString(received.ToArray(), bodyStart, Math.Max(0, received.Count - bodyStart));
        return new RecordedRequest(
            startLine.Length > 0 ? startLine[0] : string.Empty,
            startLine.Length > 1 ? startLine[1] : string.Empty,
            headers,
            body);
    }

    private static int HeaderEnd(IReadOnlyList<byte> bytes)
    {
        for (var index = 3; index < bytes.Count; index++)
        {
            if (bytes[index - 3] == '\r' && bytes[index - 2] == '\n' && bytes[index - 1] == '\r' && bytes[index] == '\n')
            {
                return index - 3;
            }
        }

        return -1;
    }

    /// <summary>Stops the listener; the serve loop observes the cancellation and ends.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        try
        {
            await _serve.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }
}

/// <summary>One request the endpoint received.</summary>
internal sealed record RecordedRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Headers,
    string Body);
