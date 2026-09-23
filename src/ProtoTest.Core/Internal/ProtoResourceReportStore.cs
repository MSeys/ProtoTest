namespace ProtoTest.Core.Internal;

/// <summary>
/// Run-scoped store for the resources tests owned, so ownership reaches the run's reports and run
/// gates.
/// </summary>
internal sealed class ProtoResourceReportStore : ProtoReportItemStore
{
    public void Add(string owner, IEnumerable<ProtoResourceSnapshot> resources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(resources);

        AddRange(resources.Select(resource =>
            resource.ToReportItem("Test resources", scope: "test", displayGroup: owner)));
    }
}
