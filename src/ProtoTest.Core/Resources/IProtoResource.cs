namespace ProtoTest.Core;

/// <summary>
/// Something the current test owns. Resources are released in reverse registration order during
/// teardown, after test hooks and attributes have run but before the test's clients are disposed.
/// </summary>
public interface IProtoResource
{
    /// <summary>Gets the stable identity of the resource, for example <c>customer:42</c>.</summary>
    string Id { get; }

    /// <summary>Gets the resource kind, for example <c>database</c> or <c>entity</c>.</summary>
    string Kind { get; }

    /// <summary>Gets a single line describing the resource for the trace, report and viewer.</summary>
    string Description { get; }

    /// <summary>Gets how long the resource lives. Defaults to the test that registered it.</summary>
    ProtoResourceScope Scope => ProtoResourceScope.Test;

    /// <summary>
    /// Releases the resource. Each resource is released at most once, so implementations do not have
    /// to guard against repeated calls, but must tolerate a failed release being reported.
    /// </summary>
    ValueTask ReleaseAsync(ProtoResourceReleaseContext context);
}
