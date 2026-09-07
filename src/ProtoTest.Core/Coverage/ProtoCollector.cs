namespace ProtoTest.Core;

public abstract class ProtoCollector(string targetName) : IProtoCollector
{
    public string TargetName { get; } = targetName ?? throw new ArgumentNullException(nameof(targetName));
    public abstract string Category { get; }

#if NET9_0_OR_GREATER
    protected readonly Lock Lock = new();
#else
    protected readonly object Lock = new();
#endif

    protected readonly Dictionary<string, CoverageItem> Items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records an incoming hit. Derived collectors can override this method
    /// to perform additional aggregation.
    /// </summary>
    public virtual void RecordHit(CoverageHit hit)
    {
        lock (Lock)
        {
            if (!Items.TryGetValue(hit.Identifier, out var item))
            {
                item = new CoverageItem(TargetName, Category, hit.Identifier, IsVisited: false, HitCount: 0);
            }

            Items[hit.Identifier] = item with
            {
                IsVisited = true,
                HitCount = item.HitCount + 1,
                Metadata = MergeMetadata(item.Metadata, hit.Metadata)
            };
        }
    }

    /// <summary>
    /// Returns a snapshot of the collected report items. Derived collectors can
    /// override this method for custom aggregation or hierarchical reports.
    /// </summary>
    public virtual IEnumerable<CoverageItem> GetReportItems()
    {
        lock (Lock)
        {
            return [.. Items.Values];
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
                continue;
            }

            if (existingValue is IEnumerable<string> existingList && incomingValue is IEnumerable<string> incomingList)
            {
                merged[key] = existingList.Union(incomingList, StringComparer.OrdinalIgnoreCase).ToList();
            }
            else if (existingValue is IEnumerable<int> existingInts && incomingValue is IEnumerable<int> incomingInts)
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