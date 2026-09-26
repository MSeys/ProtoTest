namespace ProtoTest.Core.Internal;

/// <summary>
/// A run setup step registered with <c>AddRunSetup</c>: an infrastructure piece whose start is the
/// step. It sits at the registration position, so a step registered after a container starts after
/// that container and reads the settings it published. The step is an action, not a resource, so its
/// release is a no-op and stop/dispose never call it again - the host still records it as a run
/// entity and releases that ownership period like any other piece.
/// </summary>
internal sealed class ProtoRunSetupInfrastructure(
    string name,
    Func<ProtoRunSetupContext, ValueTask> setup) : IProtoConfiguredInfrastructure
{
    public string Id => $"setup:{name}";

    public string Kind => "setup";

    public string Description => $"Run setup · {name}";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    public ValueTask StartAsync(ProtoInfrastructureContext context, CancellationToken cancellationToken = default)
        => setup(new ProtoRunSetupContext(context.Settings, context.Configuration, cancellationToken));

    /// <summary>There is nothing to release: the step owns nothing the run must dispose.</summary>
    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => ValueTask.CompletedTask;
}
