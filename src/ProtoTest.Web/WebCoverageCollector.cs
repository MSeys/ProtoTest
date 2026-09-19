namespace ProtoTest.Web;

using Microsoft.Extensions.Configuration;
using ProtoTest.Core;
using ProtoTest.Web.Internal;

/// <summary>
/// Aggregates page coverage: every page path the suite visited, verified or discovered, plus the
/// inventory in <c>ProtoTest:Web:Pages</c> and the frontend routes discovered from
/// <c>ProtoTest:Web:Pages:Source</c>. A page is covered only when a <c>web.page.verified</c> observation
/// was recorded for it; a page that was merely visited, or that only exists in the inventory, is
/// reported uncovered. A concrete path that matches an inventory pattern lands on the pattern: verifying
/// <c>/users/42</c> covers <c>/users/{id}</c>.
/// </summary>
public sealed class WebCoverageCollector : ProtoCoverageCollector
{
    private readonly List<string> _inventory = [];

    /// <summary>
    /// Creates the built-in collector under the <c>Web</c> target, reading <c>ProtoTest:Web:Pages</c>,
    /// <c>ProtoTest:Web:Pages:Source</c> and <c>ProtoTest:Web:Pages:Framework</c>.
    /// </summary>
    public WebCoverageCollector(IConfiguration configuration) : this("Web", configuration)
    {
    }

    /// <param name="targetName">The registered target name; <c>"Web"</c> for the built-in registration.</param>
    /// <param name="configuration">
    /// Supplies the explicit <c>ProtoTest:Web:Pages</c> inventory and the optional frontend folder under
    /// <c>ProtoTest:Web:Pages:Source</c>, when present. A missing folder contributes nothing.
    /// </param>
    public WebCoverageCollector(string targetName, IConfiguration? configuration = null) : base(targetName)
    {
        if (configuration is null) return;
        foreach (var value in ReadConfiguredPages(configuration))
        {
            AddInventory(WebPagePath.Normalize(value));
        }

        foreach (var value in WebPageSourceScanner.Discover(
                     configuration["ProtoTest:Web:Pages:Source"],
                     configuration["ProtoTest:Web:Pages:Framework"]))
        {
            AddInventory(value);
        }
    }

    public override string Category => "Web";

    public override bool CanCollect(ProtoObservation observation)
        => base.CanCollect(observation)
           && observation.Kind is "web.page.visited" or "web.page.verified" or "web.page.available";

    public override void Collect(ProtoObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var path = WebPagePath.Normalize(observation.Identifier);
        if (path is null) return;

        lock (_lock)
        {
            var key = ResolveKey(path);
            var item = _items.TryGetValue(key, out var existing) ? existing : CreateItem(key);
            _items[key] = observation.Kind == "web.page.verified"
                ? item with
                {
                    Status = ProtoReportStatus.Success,
                    Count = item.Count + 1,
                    IsCovered = true
                }
                : item;
        }
    }

    public override IEnumerable<ProtoReportItem> GetReportItems()
    {
        lock (_lock)
        {
            var items = new Dictionary<string, ProtoReportItem>(_items, StringComparer.OrdinalIgnoreCase);
            foreach (var path in _inventory)
            {
                if (!items.ContainsKey(path)) items[path] = CreateItem(path);
            }

            return items.Values.OrderBy(item => item.Identifier, StringComparer.Ordinal).ToArray();
        }
    }

    private ProtoReportItem CreateItem(string path)
        => new(
            TargetName,
            Category,
            path,
            Kind: ProtoReportItemKinds.Coverage,
            Status: ProtoReportStatus.Neutral,
            Count: 0,
            IsCovered: false,
            DisplayName: path);

    /// <summary>
    /// The inventory entry a concrete path belongs to: an exact entry wins, then the first matching
    /// pattern in inventory order; a catch-all (<c>{...}</c>) is only used when no non-catch-all pattern
    /// matches. Without a match the concrete path is its own item.
    /// </summary>
    private string ResolveKey(string path)
    {
        if (_items.ContainsKey(path) || _inventory.Contains(path, StringComparer.OrdinalIgnoreCase)) return path;
        string? catchAll = null;
        foreach (var pattern in _inventory)
        {
            if (!WebPagePath.Matches(pattern, path)) continue;
            if (!IsCatchAll(pattern)) return pattern;
            catchAll ??= pattern;
        }

        return catchAll ?? path;
    }

    private static bool IsCatchAll(string pattern)
        => pattern.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "{...}");

    private void AddInventory(string? path)
    {
        if (path is not null && !_inventory.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _inventory.Add(path);
        }
    }

    private static IReadOnlyList<string> ReadConfiguredPages(IConfiguration configuration)
    {
        var section = configuration.GetSection("ProtoTest:Web:Pages");
        var values = section.GetChildren()
            .Where(child => !IsInventorySetting(child.Key))
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
        if (values.Count == 0 && !string.IsNullOrWhiteSpace(section.Value))
        {
            values.Add(section.Value!);
        }

        return values;
    }

    /// <summary><c>Source</c> and <c>Framework</c> configure discovery; they are never page entries.</summary>
    private static bool IsInventorySetting(string key)
        => key.Equals("Source", StringComparison.OrdinalIgnoreCase)
           || key.Equals("Framework", StringComparison.OrdinalIgnoreCase);
}
