namespace ProtoTest.Hosting;

/// <summary>
/// Configuration a suite applies to a worker host on top of the settings the run provides (started
/// infrastructure's connection strings and the suite's own configuration). Values set here win over both.
/// </summary>
public sealed class ProtoWorkerOptions
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

    internal IReadOnlyDictionary<string, string?> Values => _values;

    /// <summary>
    /// Sets one configuration key the worker reads, for example <c>Worker:RetryLimit</c>. A
    /// <see langword="null"/> value removes nothing; it stays an empty setting.
    /// </summary>
    public ProtoWorkerOptions Set(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _values[key] = value;
        return this;
    }
}
