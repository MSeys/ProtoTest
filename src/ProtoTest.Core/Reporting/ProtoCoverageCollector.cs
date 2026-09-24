namespace ProtoTest.Core;

using ProtoTest.Core.Internal;

/// <summary>
/// Thread-safe base for collectors that aggregate observations as coverage items.
/// </summary>
public abstract class ProtoCoverageCollector : IProtoCollector, IProtoReportSource
{
    private readonly string? _kind;

    /// <summary>Creates a collector for one target, accepting every observation kind it sees.</summary>
    protected ProtoCoverageCollector(string targetName)
        : this(targetName, kind: null, identifierComparer: null)
    {
    }

    /// <summary>
    /// Creates a collector for one target and, when <paramref name="kind"/> is given, one observation
    /// kind. <paramref name="identifierComparer"/> chooses whether identifiers are case-sensitive:
    /// GraphQL operation names are, most coverage identifiers are not.
    /// </summary>
    protected ProtoCoverageCollector(string targetName, string? kind, StringComparer? identifierComparer = null)
    {
        TargetName = targetName ?? throw new ArgumentNullException(nameof(targetName));
        _kind = kind;
        _items = new Dictionary<string, ProtoReportItem>(identifierComparer ?? StringComparer.OrdinalIgnoreCase);
    }

    public string TargetName { get; }
    public abstract string Category { get; }

    protected readonly ProtoLock _lock = new();

    protected readonly Dictionary<string, ProtoReportItem> _items;

    public virtual bool CanCollect(ProtoObservation observation)
        => string.Equals(TargetName, observation.TargetName, StringComparison.OrdinalIgnoreCase)
           && (_kind is null || string.Equals(observation.Kind, _kind, StringComparison.Ordinal));

    public virtual void Collect(ProtoObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        lock (_lock)
        {
            if (!_items.TryGetValue(observation.Identifier, out var item))
            {
                item = new ProtoReportItem(
                    TargetName,
                    Category,
                    observation.Identifier,
                    Kind: ProtoReportItemKinds.Coverage,
                    Status: ProtoReportStatus.Neutral,
                    IsCovered: false);
            }

            _items[observation.Identifier] = item with
            {
                Status = ProtoReportStatus.Success,
                Count = item.Count + 1,
                IsCovered = true,
                Metadata = MergeMetadata(item.Metadata, ProtoMetadataRedaction.Redact(observation.Metadata))
            };
        }
    }

    public virtual IEnumerable<ProtoReportItem> GetReportItems()
    {
        lock (_lock)
        {
            return [.. _items.Values];
        }
    }

    protected static IReadOnlyDictionary<string, object>? MergeMetadata(
        IReadOnlyDictionary<string, object>? existing,
        IReadOnlyDictionary<string, object>? incoming)
    {
        if (existing is null) return incoming;
        if (incoming is null) return existing;

        var merged = new Dictionary<string, object>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, incomingValue) in incoming)
        {
            if (!merged.TryGetValue(key, out var existingValue))
            {
                merged[key] = incomingValue;
            }
            else if (existingValue is IEnumerable<string> existingStrings
                     && incomingValue is IEnumerable<string> incomingStrings)
            {
                merged[key] = existingStrings.Union(incomingStrings, StringComparer.OrdinalIgnoreCase).ToList();
            }
            else if (existingValue is IEnumerable<int> existingInts
                     && incomingValue is IEnumerable<int> incomingInts)
            {
                merged[key] = existingInts.Union(incomingInts).ToList();
            }
            else
            {
                merged[key] = incomingValue;
            }
        }

        return merged;
    }
}
