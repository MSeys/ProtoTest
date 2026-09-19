namespace ProtoTest.Core.Internal;

/// <summary>Owns the resources that live for a whole run and releases them when the host is disposed.</summary>
internal sealed class ProtoRunResourceStore : IProtoReportSource
{
    private readonly ProtoResourceRegistry _resources = new();

    public void Add(IProtoResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (resource.Scope != ProtoResourceScope.Run)
        {
            throw new ArgumentException(
                $"Resource '{resource.Id}' is {resource.Scope}-scoped; only run-scoped resources belong to the host.",
                nameof(resource));
        }

        // Re-adding the identical instance is a no-op: repeated integration registration must be safe,
        // and the first registration owns the resource's lifecycle. A different instance under the same
        // id is a conflict and must stay one.
        var existing = _resources.Resources.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, resource.Id, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (ReferenceEquals(existing, resource))
            {
                return;
            }

            throw new InvalidOperationException(
                $"A resource with id '{resource.Id}' is already owned by the run.");
        }

        _resources.Register(resource);
    }

    /// <summary>
    /// Re-owns the run's resources after a failed start released them, so a retried start can release
    /// them again when the run actually ends.
    /// </summary>
    public void ResetForRestart() => _resources.ResetForRestart();

    public IReadOnlyList<ProtoResourceSnapshot> Snapshot() => _resources.Snapshot();

    public IReadOnlyList<IProtoResource> Resources => _resources.Resources;

    public bool HasResources => Resources.Count > 0;

    public ValueTask<IReadOnlyList<Exception>> ReleaseAllAsync(IProtoTraceWriter trace)
        => _resources.ReleaseAllAsync(test: null, trace, ProtoTracePhase.Run);

    public IEnumerable<ProtoReportItem> GetReportItems()
        => [.. Snapshot().Select(resource => resource.ToReportItem("Run resources", scope: "run", displayGroup: null))];
}
