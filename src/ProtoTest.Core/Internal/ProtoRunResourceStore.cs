namespace ProtoTest.Core.Internal;

/// <summary>Owns the resources that live for a whole run and releases them when the host is disposed.</summary>
internal sealed class ProtoRunResourceStore
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

        _resources.Register(resource);
    }

    public IReadOnlyList<ProtoResourceSnapshot> Snapshot() => _resources.Snapshot();

    public ValueTask<IReadOnlyList<Exception>> ReleaseAllAsync(IProtoTraceWriter trace)
        => _resources.ReleaseAllAsync(test: null, trace, ProtoTracePhase.Run);
}
