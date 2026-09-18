namespace ProtoTest.Core;

/// <summary>
/// Thread-safe base for collectors that aggregate observations as coverage items.
/// </summary>
public abstract class ProtoCoverageCollector(string targetName) : IProtoCollector, IProtoReportSource
{
    public string TargetName { get; } = targetName ?? throw new ArgumentNullException(nameof(targetName));
    public abstract string Category { get; }

    protected readonly ProtoLock _lock = new();

    protected readonly Dictionary<string, ProtoReportItem> _items = new(StringComparer.OrdinalIgnoreCase);

    public virtual bool CanCollect(ProtoObservation observation)
        => string.Equals(TargetName, observation.TargetName, StringComparison.OrdinalIgnoreCase);

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
                Metadata = MergeMetadata(item.Metadata, observation.Metadata)
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
