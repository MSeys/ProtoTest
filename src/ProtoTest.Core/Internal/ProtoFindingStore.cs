namespace ProtoTest.Core.Internal;

/// <summary>
/// Run-scoped store for findings recorded by tests and hooks, so findings reach the run's reports and
/// run gates.
/// </summary>
internal sealed class ProtoFindingStore : ProtoReportItemStore;
