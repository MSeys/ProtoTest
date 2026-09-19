namespace ProtoTest.Demo;

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
        // would deadlock the run. The handlers keep both streams flowing; the output is diagnostic only.
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        using var client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
        for (var attempt = 0; attempt < 50; attempt++)
        {
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

            await Task.Delay(200, cancellationToken);
        }

        throw new InvalidOperationException("The standalone sample application did not become healthy.");
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
    {
        _process?.Kill(entireProcessTree: true);
        _process?.Dispose();
        _process = null;
        return ValueTask.CompletedTask;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
