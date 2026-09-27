namespace ProtoTest.Aspire.Internal;

using ProtoTest.Core;

/// <summary>
/// The AppHosts a run composed, so a test can resolve a resource to the key its value was published
/// under: the application's <c>BaseUrl</c> for an endpoint resource, the target's declared key for a
/// connection string.
/// </summary>
internal sealed class ProtoAspireRegistry
{
    private readonly ProtoLock _gate = new();
    private readonly Dictionary<string, ResourceHandle> _resources = new(StringComparer.Ordinal);

    public void Add(Type entryPoint, string resource, string key)
    {
        lock (_gate)
        {
            var owner = OwnerOfLocked(resource);
            if (owner is not null && owner != entryPoint)
            {
                throw new InvalidOperationException(
                    $"Aspire resource '{resource}' is already hosted by {owner.FullName}; " +
                    $"register {entryPoint.FullName} under a different resource name instead.");
            }

            _resources[resource] = new ResourceHandle(entryPoint, key);
        }
    }

    internal Type? OwnerOf(string resource)
    {
        lock (_gate)
        {
            return OwnerOfLocked(resource);
        }
    }

    private Type? OwnerOfLocked(string resource)
        => _resources.TryGetValue(resource, out var existing) ? existing.EntryPoint : null;

    public string KeyFor(string resource)
    {
        lock (_gate)
        {
            if (!_resources.TryGetValue(resource, out var handle))
            {
                var known = _resources.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
                var hint = known.Length == 0
                    ? "register one with builder.AddAspireAppHost<TEntryPoint>(\"resource\")"
                    : $"known resources: {string.Join(", ", known.Select(name => $"'{name}'"))}";
                throw new InvalidOperationException(
                    $"Unknown Aspire resource '{resource}'; {hint}.");
            }

            return handle.Key;
        }
    }

    private sealed record ResourceHandle(Type EntryPoint, string Key);
}
