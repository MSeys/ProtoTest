namespace ProtoTest.Hosting.TestWorker;

using System.Collections.Concurrent;
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
        WorkerMainCapture.Capture(builder.Configuration);
        builder.Services.AddSingleton<WorkerProbe>();
        builder.Services.AddHostedService<ProbeWorker>();
        builder.Build().Run();
    }
}

/// <summary>
/// Captures what the worker's <see cref="Program.Main"/> read from its own configuration before the host
/// was built, keyed by a probe id the starting test sets on its flow. The run hands the merged overlay
/// to the entry point as command-line arguments, so a worker that builds from its args
/// sees final-precedence values inside <c>Main</c>; this probe is how that is asserted.
/// </summary>
public static class WorkerMainCapture
{
    private static readonly AsyncLocal<string?> ProbeId = new();
    private static readonly ConcurrentDictionary<string, WorkerMainProbe> Probes = new(StringComparer.Ordinal);

    /// <summary>Marks the current flow as the one a worker's entry point will run on.</summary>
    public static IDisposable Begin(string probeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeId);
        var previous = ProbeId.Value;
        ProbeId.Value = probeId;
        return new Scope(previous);
    }

    /// <summary>What <c>Main</c> saw for a probe id, or <see langword="null"/> when the entry point did not run.</summary>
    public static WorkerMainProbe? Find(string probeId) =>
        Probes.TryGetValue(probeId, out var probe) ? probe : null;

    internal static void Capture(IConfiguration configuration)
    {
        if (ProbeId.Value is not { } probeId)
        {
            return;
        }

        Probes[probeId] = new WorkerMainProbe(
            configuration["Worker:Value"],
            configuration["Worker:FromConfig"],
            configuration["Worker:FromRun"],
            configuration["Worker:Probe"],
            configuration.GetConnectionString("WorkerProbe"));
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                ProbeId.Value = previous;
            }
        }
    }
}

/// <summary>
/// A second entry-point-shaped type in this assembly, for the same-name/different-program guard:
/// <c>AddWorkerHost</c> resolves the host factory from the assembly's entry point, so any class
/// in the assembly can stand in for a different program in a registration-conflict test.
/// </summary>
public sealed class OtherWorkerProgram;

/// <summary>What the worker's entry point read in <c>Main</c>, before its host was built.</summary>
public sealed record WorkerMainProbe(
    string? Value,
    string? ConfigValue,
    string? RunValue,
    string? Probe,
    string? ConnectionString);

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
