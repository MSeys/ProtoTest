namespace ProtoTest.Verification;

using System.Text.RegularExpressions;
using ProtoTest.Traces;

/// <summary>How a test's outcome moved between the baseline run and the current run.</summary>
public static class ProtoTestChanges
{
    /// <summary>Did not succeed in the baseline and succeeded now.</summary>
    public const string Fixed = "fixed";

    /// <summary>Succeeded in the baseline and did not succeed now.</summary>
    public const string Broken = "broken";

    /// <summary>Did not succeed in either run.</summary>
    public const string StillFailing = "still-failing";

    /// <summary>Neither run failed it (a skip is not a failure), or it failed and is now skipped.</summary>
    public const string Unchanged = "unchanged";

    /// <summary>Only the current run recorded it.</summary>
    public const string New = "new";

    /// <summary>Only the baseline run recorded it.</summary>
    public const string Removed = "removed";
}

/// <summary>Why two paired operations count as the point where the runs diverge.</summary>
public static class ProtoDivergenceReasons
{
    /// <summary>The paired operation recorded a different status.</summary>
    public const string StatusChanged = "status-changed";

    /// <summary>The paired operation failed in both runs with a different error type.</summary>
    public const string ErrorChanged = "error-changed";

    /// <summary>The baseline recorded an operation the current run did not reach.</summary>
    public const string Missing = "missing";

    /// <summary>The current run recorded an operation the baseline did not have.</summary>
    public const string Added = "added";
}

/// <summary>One operation as a comparison reports it: what it was, how it ended and where it started.</summary>
public sealed record ProtoComparedOperation(
    string Kind,
    string Name,
    string? Subject,
    string Status,
    string? ErrorType,
    string? ErrorMessage,
    double? DurationMs,
    string? SourceFile,
    int? SourceLine);

/// <summary>
/// The first point where a test's two recordings part: the reason and the operation on each side. A
/// side is null when that run did not record the operation.
/// </summary>
public sealed record ProtoOperationDivergence(
    string Reason,
    ProtoComparedOperation? Baseline,
    ProtoComparedOperation? Current);

/// <summary>One test across the two runs: its change class, both outcomes and, when they differ, where.</summary>
public sealed record ProtoTestComparison(
    string Name,
    string Change,
    string? BaselineOutcome,
    string? CurrentOutcome,
    ProtoOperationDivergence? Divergence);

/// <summary>Two runs compared test by test. Tests are listed broken first, then fixed, then the rest.</summary>
public sealed record ProtoTraceComparison(
    string BaselineRunId,
    string CurrentRunId,
    IReadOnlyList<ProtoTestComparison> Tests)
{
    /// <summary>How many tests fall in each change class.</summary>
    public IReadOnlyDictionary<string, int> Counts { get; } = Tests
        .GroupBy(test => test.Change, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    /// <summary>True when a test that succeeded in the baseline does not succeed now.</summary>
    public bool HasBroken => Tests.Any(test => test.Change == ProtoTestChanges.Broken);
}

/// <summary>
/// Compares two recorded runs test by test. Tests match by name, because a test id carries its run's
/// prefix. Operations pair by kind, name and subject in recorded order, with the values that change on
/// every run (ports, ids, long numbers) masked. The divergence is the first operation in the test body
/// whose status or error type differs; only when none does, the first operation one run did not record.
/// </summary>
internal static partial class ProtoTraceComparer
{
    private static readonly Dictionary<string, int> ChangeOrder = new(StringComparer.Ordinal)
    {
        [ProtoTestChanges.Broken] = 0,
        [ProtoTestChanges.Fixed] = 1,
        [ProtoTestChanges.StillFailing] = 2,
        [ProtoTestChanges.New] = 3,
        [ProtoTestChanges.Removed] = 4,
        [ProtoTestChanges.Unchanged] = 5
    };

    public static ProtoTraceComparison Compare(ProtoTraceArchive baseline, ProtoTraceArchive current)
    {
        var baselineTests = ByName(baseline.Tests);
        var currentTests = ByName(current.Tests);
        var comparisons = new List<ProtoTestComparison>();
        foreach (var (key, test) in currentTests)
        {
            comparisons.Add(baselineTests.TryGetValue(key, out var before)
                ? CompareTest(before, test)
                : new ProtoTestComparison(test.Name, ProtoTestChanges.New, null, test.Outcome, null));
        }

        foreach (var (key, test) in baselineTests)
        {
            if (!currentTests.ContainsKey(key))
            {
                comparisons.Add(new ProtoTestComparison(test.Name, ProtoTestChanges.Removed, test.Outcome, null, null));
            }
        }

        comparisons.Sort((left, right) =>
        {
            var byChange = ChangeOrder[left.Change].CompareTo(ChangeOrder[right.Change]);
            return byChange != 0 ? byChange : string.CompareOrdinal(left.Name, right.Name);
        });

        return new ProtoTraceComparison(baseline.RunId, current.RunId, comparisons);
    }

    private static ProtoTestComparison CompareTest(ProtoTraceTest baseline, ProtoTraceTest current)
    {
        var change = (IsFailing(baseline), IsFailing(current)) switch
        {
            (true, false) when current.Succeeded => ProtoTestChanges.Fixed,
            (false, true) => ProtoTestChanges.Broken,
            (true, true) => ProtoTestChanges.StillFailing,
            _ => ProtoTestChanges.Unchanged
        };

        // Two green recordings differ only in timing and ids; reading a divergence there would be noise.
        var divergence = change == ProtoTestChanges.Unchanged ? null : FirstDivergence(baseline, current);
        return new ProtoTestComparison(current.Name, change, baseline.Outcome, current.Outcome, divergence);
    }

    /// <summary>A test fails when it neither succeeded nor was skipped; a skip is planned, not a failure.</summary>
    internal static bool IsFailing(ProtoTraceTest test)
        => !test.Succeeded && !string.Equals(test.Outcome, "skipped", StringComparison.Ordinal);

    private static ProtoOperationDivergence? FirstDivergence(ProtoTraceTest baseline, ProtoTraceTest current)
    {
        var baselineOperations = Ordered(baseline);
        var paired = new HashSet<ProtoTraceOperation>(ReferenceEqualityComparer.Instance);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var baselineBySignature = baselineOperations
            .GroupBy(Signature, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        // Every difference is a candidate; the best rank wins, the earliest among equals.
        var candidates = new List<(int Rank, ProtoOperationDivergence Divergence)>();
        foreach (var operation in Ordered(current))
        {
            var signature = Signature(operation);
            var index = occurrences.TryGetValue(signature, out var seen) ? seen : 0;
            occurrences[signature] = index + 1;
            if (baselineBySignature.TryGetValue(signature, out var matches) && index < matches.Count)
            {
                var before = matches[index];
                paired.Add(before);
                if (Differ(before, operation) is { } changed)
                {
                    candidates.Add((Rank(operation, changed: true), changed));
                }
            }
            else
            {
                candidates.Add((Rank(operation, changed: false), new ProtoOperationDivergence(ProtoDivergenceReasons.Added, null, Describe(operation))));
            }
        }

        foreach (var operation in baselineOperations.Where(operation => !paired.Contains(operation)))
        {
            candidates.Add((Rank(operation, changed: false), new ProtoOperationDivergence(ProtoDivergenceReasons.Missing, Describe(operation), null)));
        }

        return candidates.Count == 0 ? null : candidates.MinBy(candidate => candidate.Rank).Divergence;
    }

    // A changed status in the test body says the most; an operation only one run recorded, outside the
    // body, the least. Phase spans wrap everything, so they come last.
    private static int Rank(ProtoTraceOperation operation, bool changed)
    {
        if (IsPhase(operation))
        {
            return 9;
        }

        var body = string.Equals(operation.Phase, "execution", StringComparison.Ordinal);
        return (changed, body) switch
        {
            (true, true) => 0,
            (true, false) => 1,
            (false, true) => 2,
            _ => 3
        };
    }

    private static ProtoOperationDivergence? Differ(ProtoTraceOperation baseline, ProtoTraceOperation current)
    {
        if (!string.Equals(baseline.Status, current.Status, StringComparison.Ordinal))
        {
            return new ProtoOperationDivergence(ProtoDivergenceReasons.StatusChanged, Describe(baseline), Describe(current));
        }

        // Messages carry run-specific values (ids, ports, times), so only the error type decides.
        return (baseline.Failed || baseline.HasError)
            && !string.Equals(baseline.ErrorType, current.ErrorType, StringComparison.Ordinal)
                ? new ProtoOperationDivergence(ProtoDivergenceReasons.ErrorChanged, Describe(baseline), Describe(current))
                : null;
    }

    private static List<ProtoTraceOperation> Ordered(ProtoTraceTest test)
        => [.. test.Operations.Select((operation, index) => (operation, index))
            .OrderBy(entry => entry.operation.StartedAtUtc)
            .ThenBy(entry => entry.index)
            .Select(entry => entry.operation)];

    private static string Signature(ProtoTraceOperation operation)
        => string.Join('\u001f', operation.Kind, Mask(operation.Name), Mask(operation.Subject ?? string.Empty));

    // Ports, GUIDs, long hex ids and long numbers differ on every run; masked, the same step pairs.
    private static string Mask(string value) => RunValue().Replace(value, "#");

    [GeneratedRegex(@":\d{2,5}\b|[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}|\b[0-9a-f]{16,}\b|\d{4,}", RegexOptions.CultureInvariant)]
    private static partial Regex RunValue();

    private static bool IsPhase(ProtoTraceOperation operation)
        => operation.Kind.StartsWith("test.", StringComparison.Ordinal);

    private static ProtoComparedOperation Describe(ProtoTraceOperation operation)
        => new(
            operation.Kind,
            operation.Name,
            operation.Subject,
            operation.Status,
            operation.ErrorType,
            operation.ErrorMessage,
            operation.DurationMs,
            operation.SourceFile,
            operation.SourceLine);

    // A name recorded twice in one run (a repeated row) pairs by occurrence.
    private static Dictionary<string, ProtoTraceTest> ByName(IEnumerable<ProtoTraceTest> tests)
    {
        var byName = new Dictionary<string, ProtoTraceTest>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var test in tests)
        {
            var count = seen.TryGetValue(test.Name, out var value) ? value + 1 : 1;
            seen[test.Name] = count;
            byName[count == 1 ? test.Name : $"{test.Name}\u001f{count}"] = test;
        }

        return byName;
    }
}
