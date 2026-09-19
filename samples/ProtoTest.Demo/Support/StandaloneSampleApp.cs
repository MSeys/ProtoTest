namespace ProtoTest.Demo;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using ProtoTest.Core;

/// <summary>
/// Starts the sample application as a standalone process over the suite's store, so browser tests have a
/// real address. It is run-scoped infrastructure: the host starts it, exposes its address as the web
/// session's base URL, and releases it with the run - the journey file only contains the journey.
/// </summary>
internal sealed class StandaloneSampleApp(
    string connectionString,
    string databaseProvider = "sqlite",
    Func<string?>? messagingConnection = null) : IProtoSettingsInfrastructure
{
    private const int OutputTailLines = 40;
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentQueue<string> _output = new();
    private Process? _process;
    private string _baseUrl = string.Empty;

    public string Id => "application:northstar-standalone";

    public string Kind => "application";

    public string Description => "Northstar standalone application";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["ProtoTest:Web:Sessions:Default:BaseUrl"] = _baseUrl
    };

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        var port = FreePort();
        _baseUrl = $"http://127.0.0.1:{port}";
        var start = new ProcessStartInfo(
            "dotnet",
            $"\"{Path.Combine(AppContext.BaseDirectory, "ProtoTest.SampleApp.dll")}\"")
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["ASPNETCORE_URLS"] = _baseUrl;
        start.Environment["ConnectionStrings__Northstar"] = connectionString;
        start.Environment["Database__Provider"] = databaseProvider;
        start.Environment["ProtoTest__TestSupport"] = "true";
        // The UI instance reads the store; the suite's in-process instance owns webhook dispatch.
        start.Environment["Northstar__DisableWebhookDispatcher"] = "true";
        // The built console lives in the repository, not in the test output, so the absolute path is
        // passed as process environment: the standalone app serves the same build the in-process one does.
        if (ConsoleBuild.DistFolder is { } dist)
        {
            start.Environment["Northstar__Ui__Path"] = dist;
        }

        // Paying an invoice in the browser publishes through the same broker the tests await on.
        if (messagingConnection?.Invoke() is { Length: > 0 } broker)
        {
            start.Environment["Messaging__RabbitMq__ConnectionString"] = broker;
        }

        _process = Process.Start(start)!;
        // Redirected pipes fill up and block the child once nobody drains them, so a chatty application
        // would deadlock the run. The handlers keep both streams flowing and remember the tail, so a
        // failure to become healthy can report why instead of a bare timeout.
        _process.OutputDataReceived += (_, e) => Remember(e.Data);
        _process.ErrorDataReceived += (_, e) => Remember(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var client = new HttpClient
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(5)
        };
        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"The standalone sample application exited with code {_process.ExitCode} before becoming " +
                    $"healthy at {_baseUrl}.{Environment.NewLine}Last output:{Environment.NewLine}{Tail()}");
            }

            try
            {
                using var response = await client.GetAsync("/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Still starting.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // The per-request timeout elapsed; the process may still be starting.
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException(
            $"The standalone sample application did not become healthy at {_baseUrl} within " +
            $"{(int)StartupTimeout.TotalSeconds}s.{Environment.NewLine}Last output:{Environment.NewLine}{Tail()}");
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
    {
        _process?.Kill(entireProcessTree: true);
        _process?.Dispose();
        _process = null;
        return ValueTask.CompletedTask;
    }

    private void Remember(string? line)
    {
        if (line is null)
        {
            return;
        }

        _output.Enqueue(line);
        while (_output.Count > OutputTailLines && _output.TryDequeue(out _))
        {
        }
    }

    private string Tail() => _output.IsEmpty ? "(no output)" : string.Join(Environment.NewLine, _output);

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
