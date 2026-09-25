namespace ProtoTest.Hosting.TestWorker;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>
/// A worker application shaped like a real one: it builds its own host from the command line and runs a
/// hosted service. The suite starts this through its entry point, so nothing here knows about ProtoTest.
/// </summary>
public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton<WorkerProbe>();
        builder.Services.AddHostedService<ProbeWorker>();
        builder.Build().Run();
    }
}

/// <summary>What the worker's hosted service observed, for tests to assert against.</summary>
public sealed class WorkerProbe
{
    private readonly object _gate = new();
    private bool _started;
    private bool _stopped;
    private string? _value;
    private string? _configValue;
    private string? _connectionString;
    private DateTimeOffset _startedAtUtc;

    public bool Started
    {
        get
        {
            lock (_gate)
            {
                return _started;
            }
        }
    }

    public bool Stopped
    {
        get
        {
            lock (_gate)
            {
                return _stopped;
            }
        }
    }

    /// <summary>The <c>Worker:Value</c> setting the worker read at start, on top of its own appsettings.</summary>
    public string? Value
    {
        get
        {
            lock (_gate)
            {
                return _value;
            }
        }
    }

    /// <summary>The <c>Worker:FromConfig</c> setting, only the suite's configuration provides it.</summary>
    public string? ConfigValue
    {
        get
        {
            lock (_gate)
            {
                return _configValue;
            }
        }
    }

    /// <summary>What the worker's <see cref="TimeProvider"/> reported when the service started.</summary>
    public DateTimeOffset StartedAtUtc
    {
        get
        {
            lock (_gate)
            {
                return _startedAtUtc;
            }
        }
    }

    /// <summary>The <c>WorkerProbe</c> connection string the worker read at start.</summary>
    public string? ConnectionString
    {
        get
        {
            lock (_gate)
            {
                return _connectionString;
            }
        }
    }

    internal void MarkStarted(string? value, string? connectionString, string? configValue, DateTimeOffset startedAtUtc)
    {
        lock (_gate)
        {
            _started = true;
            _value = value;
            _connectionString = connectionString;
            _configValue = configValue;
            _startedAtUtc = startedAtUtc;
        }
    }

    internal void MarkStopped()
    {
        lock (_gate)
        {
            _stopped = true;
        }
    }
}

/// <summary>The worker's hosted service; the <c>Worker:Fail*</c> settings make it throw on purpose.</summary>
public sealed class ProbeWorker(
    WorkerProbe probe,
    IConfiguration configuration,
    TimeProvider timeProvider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration.GetValue<bool>("Worker:FailStart"))
        {
            throw new InvalidOperationException("The probe worker was told to fail at start.");
        }

        probe.MarkStarted(
            configuration["Worker:Value"],
            configuration.GetConnectionString("WorkerProbe"),
            configuration["Worker:FromConfig"],
            timeProvider.GetUtcNow());
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (configuration.GetValue<bool>("Worker:FailStop"))
        {
            throw new InvalidOperationException("The probe worker was told to fail at stop.");
        }

        probe.MarkStopped();
        return Task.CompletedTask;
    }
}
