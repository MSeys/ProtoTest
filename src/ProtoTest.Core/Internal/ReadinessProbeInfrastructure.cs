namespace ProtoTest.Core.Internal;

/// <summary>
/// A readiness check registered with <c>AddReadinessProbe</c>: an infrastructure piece whose start is
/// the wait itself. It sits at the registration position, so a probe registered after a container runs
/// after that container started, and its evidence lands on the run entity the host records.
/// </summary>
internal sealed class ReadinessProbeInfrastructure : IProtoInfrastructure, IProtoStartupEvidence
{
    private readonly Func<CancellationToken, ValueTask<bool>> _probe;
    private readonly ProtoReadinessOptions _options;
    private readonly TimeSpan? _timeoutOverride;
    private Dictionary<string, string?> _evidence = new(StringComparer.Ordinal);

    public ReadinessProbeInfrastructure(
        string name,
        Func<CancellationToken, ValueTask<bool>> probe,
        ProtoReadinessOptions options,
        TimeSpan? timeoutOverride)
    {
        Name = name;
        _probe = probe;
        _options = options;
        _timeoutOverride = timeoutOverride;
    }

    private string Name { get; }

    public string Id => $"readiness:{Name}";

    public string Kind => "readiness";

    public string Description => $"Readiness · {Name}";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public IReadOnlyDictionary<string, string?> StartupEvidence => _evidence;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        var timeout = _timeoutOverride ?? _options.Timeout;
        _options.Validate();
        var result = await ProtoReadiness.WaitAsync(Name, _probe, timeout, _options.Interval, cancellationToken).ConfigureAwait(false);
        _evidence = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["readiness.attempts"] = result.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["readiness.waitedMs"] = ((long)result.Waited.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
}
