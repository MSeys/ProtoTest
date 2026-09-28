namespace ProtoTest.Traces;

/// <summary>One recorded change of a tracked item, as the state document carries it.</summary>
public sealed record ProtoTraceStateChange(
    DateTimeOffset? AtUtc,
    string? OperationId,
    string Change,
    IReadOnlyDictionary<string, string?> State,
    string Source,
    bool Inferred);

/// <summary>
/// One tracked item (a client, context, resource, capability or application value) with its latest
/// state and every recorded change.
/// </summary>
public sealed record ProtoTraceStateItem(
    string Kind,
    string Id,
    string Name,
    string? Scope,
    DateTimeOffset? FirstSeenUtc,
    DateTimeOffset? LastSeenUtc,
    IReadOnlyDictionary<string, string?> State,
    IReadOnlyList<ProtoTraceStateChange> Changes);

/// <summary>One test's tracked items, keyed by the test id the spans document uses.</summary>
public sealed record ProtoTraceStateTest(
    string TestId,
    string Name,
    IReadOnlyList<ProtoTraceStateItem> Items);

/// <summary>
/// The run's state document: the items the run and each test tracked, with their changes. Read on
/// request, because a consumer that only lists runs or failures never needs it.
/// </summary>
public sealed record ProtoTraceState(
    string FormatVersion,
    IReadOnlyList<ProtoTraceStateItem> RunItems,
    IReadOnlyList<ProtoTraceStateTest> Tests)
{
    /// <summary>The items one test tracked; empty when the state document has no entry for it.</summary>
    public IReadOnlyList<ProtoTraceStateItem> ItemsFor(string testId)
        => Tests.FirstOrDefault(test => string.Equals(test.TestId, testId, StringComparison.Ordinal))?.Items ?? [];
}
