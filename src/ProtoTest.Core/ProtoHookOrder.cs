namespace ProtoTest.Core;

/// <summary>
/// Named positions for <see cref="IProtoTestHook.Order"/> and <see cref="IProtoRunHook.Order"/>, so a
/// hook can sit relative to the built-ins without spelling out their raw values. Lower orders run
/// earlier on the way in and later on the way out; two orders of the same value run in registration order.
/// </summary>
public static class ProtoHookOrder
{
    /// <summary>
    /// The outermost band, used by ProtoTest itself: the test hook that initializes clients runs first
    /// on the way in, and the run hook that writes the trace archive runs last on the way out.
    /// </summary>
    public const int First = int.MinValue;

    /// <summary>
    /// The test hook that completes clients, right after <see cref="First"/>, so clients outlive every
    /// other test hook.
    /// </summary>
    public const int ClientCompletion = First + 1;

    /// <summary>The run hook that releases run-scoped resources, before reports export and the trace archives.</summary>
    public const int RunResources = First + 1;

    /// <summary>The run hook that exports reports, before resources are released.</summary>
    public const int ReportSinks = First + 2;

    /// <summary>The run hook that evaluates run gates, before reports export.</summary>
    public const int RunGates = First + 3;

    /// <summary>Where hooks and attributes with no explicit order run.</summary>
    public const int Default = 0;

    /// <summary>The HTTP integrations' auth hook: after default hooks, so it can override the request.</summary>
    public const int Authentication = 100;
}
