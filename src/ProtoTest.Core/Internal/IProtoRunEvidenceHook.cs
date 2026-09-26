namespace ProtoTest.Core.Internal;

/// <summary>
/// Marks a library run hook whose <c>AfterRunAsync</c> writes run evidence: gate verdicts, report
/// exports and the trace archive. A failed start does not unwind one, because a run that never started
/// produces no evidence. Hooks that own state - user hooks and the run-resource
/// rollback - are not marked, so their <c>AfterRunAsync</c> runs when the start fails.
/// </summary>
internal interface IProtoRunEvidenceHook;
