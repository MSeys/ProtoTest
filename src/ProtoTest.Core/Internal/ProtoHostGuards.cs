namespace ProtoTest.Core.Internal;

/// <summary>
/// The guard messages both host state machines report - the run lifecycle and the host that wraps it -
/// so the same situation reads the same whichever layer noticed it first.
/// </summary>
internal static class ProtoHostGuards
{
    public const string StartupInProgress = "ProtoHost startup is already in progress.";

    public const string StartupStillInProgressStop =
        "ProtoHost startup is still in progress; stop it after startup completes.";

    public const string StartupStillInProgressDispose =
        "ProtoHost startup is still in progress; dispose it after startup completes.";
}
