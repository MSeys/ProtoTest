namespace ProtoTest.Core.Internal;

/// <summary>
/// Runs a run's lifecycle hooks in order. The host owns the run state machine; this only sequences the
/// hooks, so the guards and transitions exist in one place.
/// </summary>
internal sealed class ProtoRunHooks(IEnumerable<IProtoRunHook> hooks)
{
    private readonly IReadOnlyList<IProtoRunHook> _hooks = [.. hooks.OrderBy(hook => hook.Order)];

    /// <summary>
    /// Runs the BeforeRun hooks in ascending order, adding each hook that completed to
    /// <paramref name="completed"/> as it goes, so a failure halfway still rolls back what ran.
    /// </summary>
    public async Task RunBeforeAsync(List<IProtoRunHook> completed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(completed);
        foreach (var hook in _hooks)
        {
            await hook.BeforeRunAsync(cancellationToken);
            completed.Add(hook);
        }
    }

    /// <summary>Runs every hook's AfterInfrastructure in ascending order; the first failure stops the start.</summary>
    public async Task RunAfterInfrastructureAsync(ProtoRunSetupContext context)
    {
        foreach (var hook in _hooks)
        {
            await hook.AfterInfrastructureAsync(context);
        }
    }

    /// <summary>
    /// Runs the completed hooks' AfterRun in reverse, collecting failures instead of stopping at the
    /// first, so one failing hook cannot skip the releases behind it.
    /// </summary>
    public async Task RunAfterAsync(
        IReadOnlyList<IProtoRunHook> started,
        List<Exception> exceptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(started);
        ArgumentNullException.ThrowIfNull(exceptions);
        foreach (var hook in started.Reverse())
        {
            await LifecycleExceptionHelper.CaptureAsync(
                () => hook.AfterRunAsync(cancellationToken), exceptions);
        }
    }
}
