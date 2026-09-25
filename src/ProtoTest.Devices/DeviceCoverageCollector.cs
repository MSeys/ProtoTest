namespace ProtoTest.Devices;

using ProtoTest.Core;

/// <summary>
/// Coverage of a device protocol: every message kind the suite asserted counts as covered, and every
/// kind a registered <see cref="IProtoDeviceProtocol"/> declares but no test asserted is reported as a
/// gap - the OpenAPI treatment for a device protocol.
/// </summary>
public sealed class DeviceCoverageCollector : ProtoCoverageCollector
{
    private readonly IReadOnlyList<IProtoDeviceProtocol> _protocols;

    /// <summary>Creates the collector for a target, accepting every observation kind it sees.</summary>
    public DeviceCoverageCollector(string targetName = "Devices")
        : this(targetName, protocols: null)
    {
    }

    /// <summary>
    /// Creates the collector with the protocol catalogs whose untested message kinds it reports.
    /// </summary>
    public DeviceCoverageCollector(string targetName, IEnumerable<IProtoDeviceProtocol>? protocols)
        : base(targetName, kind: "device.operation")
    {
        _protocols = protocols?.ToArray() ?? [];
    }

    /// <inheritdoc />
    public override string Category => "Device operations";

    /// <inheritdoc />
    public override IEnumerable<ProtoReportItem> GetReportItems()
    {
        var items = base.GetReportItems().ToList();
        var covered = new HashSet<string>(items.Select(item => item.Identifier), StringComparer.OrdinalIgnoreCase);
        foreach (var protocol in _protocols)
        {
            foreach (var entry in protocol.Entries)
            {
                if (covered.Contains(entry.Kind))
                {
                    continue;
                }

                items.Add(new ProtoReportItem(
                    TargetName,
                    Category,
                    entry.Kind,
                    Kind: ProtoReportItemKinds.Coverage,
                    Status: ProtoReportStatus.Neutral,
                    IsCovered: false,
                    Message: entry.Description is null
                        ? $"{protocol.Name} · never asserted"
                        : $"{protocol.Name} · {entry.Description}"));
            }
        }

        return items;
    }
}
