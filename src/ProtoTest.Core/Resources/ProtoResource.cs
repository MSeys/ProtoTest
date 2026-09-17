namespace ProtoTest.Core;

/// <summary>A resource described by a release callback, for cases that do not warrant a type of their own.</summary>
public sealed class ProtoResource : IProtoResource
{
    private readonly Func<ProtoResourceReleaseContext, ValueTask> _release;

    public ProtoResource(
        string id,
        string kind,
        string description,
        Func<ProtoResourceReleaseContext, ValueTask> release,
        ProtoResourceScope scope = ProtoResourceScope.Test)
    {
        Scope = scope;
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(release);
        Id = id;
        Kind = kind;
        Description = description;
        _release = release;
    }

    public string Id { get; }

    public string Kind { get; }

    public string Description { get; }

    public ProtoResourceScope Scope { get; }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return _release(context);
    }

    /// <summary>Creates a resource with an asynchronous release callback.</summary>
    public static ProtoResource From(
        string id,
        string kind,
        string description,
        Func<ProtoResourceReleaseContext, CancellationToken, ValueTask> release)
    {
        ArgumentNullException.ThrowIfNull(release);
        return new ProtoResource(id, kind, description, context => release(context, context.CancellationToken));
    }
}
