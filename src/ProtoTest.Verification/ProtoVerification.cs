namespace ProtoTest.Verification;

using System.Text.Json;
using ProtoTest.Core;
using ProtoTest.Reporting;

/// <summary>One side of a comparison: the report a run produced and how the verdict names it.</summary>
/// <param name="Report">The run's report, as the reporting sink recorded it.</param>
/// <param name="RunId">The run's id, when the caller knows it; it becomes the evidence label.</param>
/// <param name="ReportPath">Where the report file came from, when it was read from disk.</param>
public sealed record ProtoVerificationRun(ProtoReport Report, string? RunId = null, string? ReportPath = null)
{
    /// <summary>Reads a JSON report the reporting sink wrote and remembers where it came from.</summary>
    public static ProtoVerificationRun FromReportFile(string reportPath, string? runId = null)
        => new(ProtoReport.ReadJson(reportPath), runId, reportPath);
}

/// <summary>A candidate specification file the pull request carries for one target. Its content hash
/// is compared with the identity the candidate run recorded.</summary>
public sealed record ProtoSpecCandidate(string TargetName, string FilePath);

/// <summary>
/// Compares a baseline run's report with a candidate run's report. Coverage is compared with the
/// report's own arithmetic, the specification identity comes from the collectors' aggregate item, and
/// a failed run gate is surfaced as the candidate run recorded it. The verdict is deterministic and
/// <see cref="ProtoVerificationVerdict.Failed"/> is the gate.
/// </summary>
public static class ProtoVerification
{
    private static readonly IReadOnlyList<string> KnownSeverities =
        [ProtoVerificationSeverities.Fail, ProtoVerificationSeverities.Warn, ProtoVerificationSeverities.Info];

    private static readonly Dictionary<string, int> ClassOrder = new(StringComparer.Ordinal)
    {
        [ProtoVerificationFindingClasses.Regressed] = 0,
        [ProtoVerificationFindingClasses.AddedUncovered] = 1,
        [ProtoVerificationFindingClasses.StaleSpec] = 2,
        [ProtoVerificationFindingClasses.GateFailed] = 3
    };

    /// <summary>Verifies the candidate report against the baseline report.</summary>
    /// <param name="baseline">The report of the run the candidate is compared with.</param>
    /// <param name="current">The candidate run's report, the one the PR carries.</param>
    /// <param name="candidateSpecs">The specification files the PR carries, keyed by target. A target
    /// with a recorded identity but no candidate here is reported as recorded, not re-verified.</param>
    /// <param name="options">The severity of each finding class; the defaults are the PR gate.</param>
    public static ProtoVerificationVerdict Verify(
        ProtoReport baseline,
        ProtoReport current,
        IEnumerable<ProtoSpecCandidate>? candidateSpecs = null,
        ProtoVerificationOptions? options = null)
        => Verify(
            new ProtoVerificationRun(baseline ?? throw new ArgumentNullException(nameof(baseline))),
            new ProtoVerificationRun(current ?? throw new ArgumentNullException(nameof(current))),
            candidateSpecs,
            options);

    /// <summary>Verifies the candidate report against the baseline report, naming each side for the
    /// evidence pointer.</summary>
    /// <param name="baseline">The report of the run the candidate is compared with.</param>
    /// <param name="current">The candidate run's report, the one the PR carries.</param>
    /// <param name="candidateSpecs">The specification files the PR carries, keyed by target. A target
    /// with a recorded identity but no candidate here is reported as recorded, not re-verified.</param>
    /// <param name="options">The severity of each finding class; the defaults are the PR gate.</param>
    /// <exception cref="ArgumentException">A candidate names a target no specification identity was
    /// recorded for, a target is named twice, or an option names an unknown severity.</exception>
    /// <exception cref="InvalidOperationException">A candidate file does not exist.</exception>
    public static ProtoVerificationVerdict Verify(
        ProtoVerificationRun baseline,
        ProtoVerificationRun current,
        IEnumerable<ProtoSpecCandidate>? candidateSpecs = null,
        ProtoVerificationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        options ??= ProtoVerificationOptions.Default;
        ValidateSeverities(options);

        var candidates = IndexCandidates(candidateSpecs);
        var findings = new List<ProtoVerificationFinding>();

        var specChecks = CheckSpecifications(baseline, current, candidates, options, findings);
        var coverageDeltas = CompareCoverage(baseline, current, options, findings);
        findings.AddRange(GateFindings(current, options));
        findings.Sort(CompareFindings);

        return new ProtoVerificationVerdict(findings, coverageDeltas, specChecks);
    }

    private static void ValidateSeverities(ProtoVerificationOptions options)
    {
        RequireSeverity(options.RegressedSeverity, nameof(options.RegressedSeverity));
        RequireSeverity(options.AddedUncoveredSeverity, nameof(options.AddedUncoveredSeverity));
        RequireSeverity(options.StaleSpecSeverity, nameof(options.StaleSpecSeverity));
        RequireSeverity(options.GateFailureSeverity, nameof(options.GateFailureSeverity));
    }

    private static void RequireSeverity(string severity, string name)
    {
        if (!KnownSeverities.Contains(severity, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{severity}' is not a verification severity for '{name}'. Use {string.Join(", ", KnownSeverities)}.",
                name);
        }
    }

    private static Dictionary<string, ProtoSpecCandidate> IndexCandidates(IEnumerable<ProtoSpecCandidate>? candidates)
    {
        var index = new Dictionary<string, ProtoSpecCandidate>(StringComparer.OrdinalIgnoreCase);
        if (candidates is null)
        {
            return index;
        }

        foreach (var candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (string.IsNullOrWhiteSpace(candidate.TargetName) || string.IsNullOrWhiteSpace(candidate.FilePath))
            {
                throw new ArgumentException("A candidate specification needs a target name and a file path.");
            }

            if (index.TryGetValue(candidate.TargetName, out var existing))
            {
                throw new ArgumentException(
                    $"More than one candidate specification names the target '{existing.TargetName}' (also given as '{candidate.TargetName}').");
            }

            index[candidate.TargetName] = candidate;
        }

        return index;
    }

    private static IReadOnlyList<ProtoSpecCheck> CheckSpecifications(
        ProtoVerificationRun baseline,
        ProtoVerificationRun current,
        Dictionary<string, ProtoSpecCandidate> candidates,
        ProtoVerificationOptions options,
        List<ProtoVerificationFinding> findings)
    {
        var baselineSpecs = RecordedSpecs(baseline.Report);
        var checks = new List<ProtoSpecCheck>();
        var checkedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in RecordedSpecs(current.Report)
            .OrderBy(candidate => candidate.TargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Category, StringComparer.OrdinalIgnoreCase))
        {
            checkedTargets.Add(spec.TargetName);
            var baselineHash = baselineSpecs
                .FirstOrDefault(candidate => string.Equals(candidate.TargetName, spec.TargetName, StringComparison.OrdinalIgnoreCase))
                ?.Hash;
            ProtoSpecCandidate? candidate = candidates.Remove(spec.TargetName, out var given) ? given : null;
            checks.Add(EvaluateSpec(spec, baselineHash, candidate, baseline, current, options, findings));
        }

        foreach (var spec in baselineSpecs
            .Where(candidate => !checkedTargets.Contains(candidate.TargetName))
            .OrderBy(candidate => candidate.TargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Category, StringComparer.OrdinalIgnoreCase))
        {
            var message = $"The candidate report records no specification identity for target '{spec.TargetName}'; the baseline recorded sha256 '{spec.Hash}'. Specification integrity is not verified for this target.";
            checks.Add(new ProtoSpecCheck(
                spec.TargetName, spec.Category, Source: null, Hash: null, spec.Hash, CandidateHash: null,
                ProtoSpecCheckStatuses.Missing, message));
            findings.Add(new ProtoVerificationFinding(
                ProtoVerificationFindingClasses.StaleSpec,
                ProtoVerificationSeverities.Info,
                spec.TargetName,
                spec.Category,
                ProtoSpecIdentity.ReportIdentifier,
                message,
                BaselineValue: spec.Hash,
                Evidence: RunLabel(current, "current")));
        }

        if (candidates.Count > 0)
        {
            var recorded = RecordedSpecs(current.Report)
                .Select(spec => spec.TargetName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
            throw new ArgumentException(
                $"No specification identity is recorded in the current report for candidate target(s) {string.Join(", ", candidates.Keys.Select(key => $"'{key}'"))}; recorded targets: {TargetList(recorded)}.");
        }

        return checks;
    }

    private static ProtoSpecCheck EvaluateSpec(
        RecordedSpec spec,
        string? baselineHash,
        ProtoSpecCandidate? candidate,
        ProtoVerificationRun baseline,
        ProtoVerificationRun current,
        ProtoVerificationOptions options,
        List<ProtoVerificationFinding> findings)
    {
        string status;
        string? candidateHash = null;
        string message;

        if (candidate is not null)
        {
            candidateHash = ProtoSpecIdentity.Hash(ReadCandidate(candidate));
            if (string.Equals(candidateHash, spec.Hash, StringComparison.Ordinal))
            {
                status = ProtoSpecCheckStatuses.Verified;
                message = $"The candidate file '{candidate.FilePath}' matches the specification the current run recorded for target '{spec.TargetName}'.";
            }
            else
            {
                status = ProtoSpecCheckStatuses.Changed;
                message = $"The candidate file '{candidate.FilePath}' does not match the specification the current run recorded for target '{spec.TargetName}': recorded sha256 '{spec.Hash}', candidate sha256 '{candidateHash}'.";
                findings.Add(new ProtoVerificationFinding(
                    ProtoVerificationFindingClasses.StaleSpec,
                    options.StaleSpecSeverity,
                    spec.TargetName,
                    spec.Category,
                    ProtoSpecIdentity.ReportIdentifier,
                    message,
                    BaselineValue: spec.Hash,
                    CurrentValue: candidateHash,
                    Evidence: RunLabel(current, "current")));
            }
        }
        else
        {
            status = ProtoSpecCheckStatuses.Recorded;
            message = IsRemoteSource(spec.Source)
                ? $"The specification for target '{spec.TargetName}' is a remote source ('{spec.Source}'); it is recorded, not re-verified, and is never fetched."
                : $"The specification for target '{spec.TargetName}' was not re-verified: no candidate file was given for the target.";
        }

        if (baselineHash is not null && !string.Equals(baselineHash, spec.Hash, StringComparison.Ordinal))
        {
            findings.Add(new ProtoVerificationFinding(
                ProtoVerificationFindingClasses.StaleSpec,
                ProtoVerificationSeverities.Info,
                spec.TargetName,
                spec.Category,
                ProtoSpecIdentity.ReportIdentifier,
                $"The specification for target '{spec.TargetName}' changed between the baseline and the current run; coverage deltas may include contract changes.",
                BaselineValue: baselineHash,
                CurrentValue: spec.Hash,
                Evidence: $"{RunLabel(baseline, "baseline")} -> {RunLabel(current, "current")}"));
        }

        return new ProtoSpecCheck(
            spec.TargetName, spec.Category, spec.Source, spec.Hash, baselineHash, candidateHash, status, message);
    }

    private static string ReadCandidate(ProtoSpecCandidate candidate)
    {
        if (!File.Exists(candidate.FilePath))
        {
            throw new InvalidOperationException(
                $"The candidate specification for target '{candidate.TargetName}' does not exist: '{candidate.FilePath}'.");
        }

        return File.ReadAllText(candidate.FilePath);
    }

    private static bool IsRemoteSource(string source)
        => Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string TargetList(IEnumerable<string> targets)
    {
        var names = targets.ToArray();
        return names.Length == 0 ? "none" : string.Join(", ", names.Select(name => $"'{name}'"));
    }

    private static IReadOnlyList<ProtoVerificationCoverageDelta> CompareCoverage(
        ProtoVerificationRun baseline,
        ProtoVerificationRun current,
        ProtoVerificationOptions options,
        List<ProtoVerificationFinding> findings)
    {
        var baselineUnits = CoverageUnits(baseline.Report);
        var currentUnits = CoverageUnits(current.Report);
        var baselineSummaries = Summaries(baseline.Report).ToDictionary(
            summary => (summary.TargetName, summary.Category), SummaryKeyComparer.Instance);
        var currentSummaries = Summaries(current.Report).ToDictionary(
            summary => (summary.TargetName, summary.Category), SummaryKeyComparer.Instance);
        var regressed = new Dictionary<(string TargetName, string Category), int>(SummaryKeyComparer.Instance);
        var addedUncovered = new Dictionary<(string TargetName, string Category), int>(SummaryKeyComparer.Instance);

        foreach (var (key, unit) in currentUnits)
        {
            if (unit.IsCovered)
            {
                continue;
            }

            if (baselineUnits.TryGetValue(key, out var baselineUnit))
            {
                if (!baselineUnit.IsCovered)
                {
                    continue;
                }

                var group = (unit.TargetName, unit.Category);
                regressed[group] = regressed.GetValueOrDefault(group) + 1;
                findings.Add(new ProtoVerificationFinding(
                    ProtoVerificationFindingClasses.Regressed,
                    options.RegressedSeverity,
                    unit.TargetName,
                    unit.Category,
                    unit.Identifier,
                    $"Target '{unit.TargetName}' unit '{unit.Identifier}' in category '{unit.Category}' was covered in the baseline and is uncovered now.",
                    BaselineValue: "covered",
                    CurrentValue: "uncovered",
                    Evidence: $"{RunLabel(baseline, "baseline")} -> {RunLabel(current, "current")}"));
                continue;
            }

            var addedGroup = (unit.TargetName, unit.Category);
            addedUncovered[addedGroup] = addedUncovered.GetValueOrDefault(addedGroup) + 1;
            findings.Add(new ProtoVerificationFinding(
                ProtoVerificationFindingClasses.AddedUncovered,
                options.AddedUncoveredSeverity,
                unit.TargetName,
                unit.Category,
                unit.Identifier,
                $"Target '{unit.TargetName}' unit '{unit.Identifier}' in category '{unit.Category}' is uncovered in the candidate and was not in the baseline.",
                BaselineValue: "absent",
                CurrentValue: "uncovered",
                Evidence: RunLabel(current, "current")));
        }

        var keys = new HashSet<(string TargetName, string Category)>(SummaryKeyComparer.Instance);
        keys.UnionWith(baselineSummaries.Keys);
        keys.UnionWith(currentSummaries.Keys);
        return
        [
            .. keys
                .OrderBy(key => key.TargetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(key => key.Category, StringComparer.OrdinalIgnoreCase)
                .Select(key => new ProtoVerificationCoverageDelta(
                    key.TargetName,
                    key.Category,
                    baselineSummaries.GetValueOrDefault(key) ?? new ProtoCoverageSummary(key.TargetName, key.Category, Covered: 0, Total: 0),
                    currentSummaries.GetValueOrDefault(key) ?? new ProtoCoverageSummary(key.TargetName, key.Category, Covered: 0, Total: 0),
                    regressed.GetValueOrDefault(key),
                    addedUncovered.GetValueOrDefault(key)))
        ];
    }

    private static IEnumerable<ProtoVerificationFinding> GateFindings(
        ProtoVerificationRun current,
        ProtoVerificationOptions options)
    {
        foreach (var item in current.Report.Items.Flatten())
        {
            if (!item.IsKind(ProtoReportItemKinds.Gate) || item.Status != ProtoReportStatus.Error)
            {
                continue;
            }

            yield return new ProtoVerificationFinding(
                ProtoVerificationFindingClasses.GateFailed,
                options.GateFailureSeverity,
                item.TargetName,
                item.Category,
                item.Identifier,
                item.Message is { Length: > 0 } message
                    ? $"Run gate '{item.Identifier}' failed: {message}"
                    : $"Run gate '{item.Identifier}' failed.",
                CurrentValue: "failed",
                Evidence: RunLabel(current, "current"));
        }
    }

    private static int CompareFindings(ProtoVerificationFinding left, ProtoVerificationFinding right)
    {
        var byClass = ClassOrder.GetValueOrDefault(left.Class, int.MaxValue)
            .CompareTo(ClassOrder.GetValueOrDefault(right.Class, int.MaxValue));
        if (byClass != 0)
        {
            return byClass;
        }

        var byTarget = string.Compare(left.TargetName, right.TargetName, StringComparison.OrdinalIgnoreCase);
        if (byTarget != 0)
        {
            return byTarget;
        }

        var byCategory = string.Compare(left.Category ?? string.Empty, right.Category ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        return byCategory != 0
            ? byCategory
            : string.Compare(left.Identifier ?? string.Empty, right.Identifier ?? string.Empty, StringComparison.Ordinal);
    }

    private static string RunLabel(ProtoVerificationRun run, string side)
        => run.RunId is { Length: > 0 } id ? $"run '{id}'"
            : run.ReportPath is { Length: > 0 } path ? $"report '{path}'"
            : $"{side} report";

    private static IReadOnlyList<ProtoCoverageSummary> Summaries(ProtoReport report)
        => new ProtoRunGateContext(report.Items).CoverageSummaries();

    private static Dictionary<CoverageUnitKey, CoverageUnit> CoverageUnits(ProtoReport report)
    {
        var units = new Dictionary<CoverageUnitKey, CoverageUnit>(CoverageUnitKeyComparer.Instance);
        foreach (var item in report.Items.Flatten().CoverageUnits())
        {
            var key = new CoverageUnitKey(item.TargetName, item.Category, item.Identifier);
            units.TryAdd(key, new CoverageUnit(item.TargetName, item.Category, item.Identifier, item.IsCovered!.Value));
        }

        return units;
    }

    private static IReadOnlyList<RecordedSpec> RecordedSpecs(ProtoReport report)
    {
        var specs = new List<RecordedSpec>();
        foreach (var item in report.Items.Flatten())
        {
            if (!item.IsKind(ProtoReportItemKinds.Coverage) || item.IsCovered is not null)
            {
                continue;
            }

            var hash = MetadataString(item.Metadata, ProtoSpecIdentity.HashMetadataKey);
            if (hash is null)
            {
                continue;
            }

            var source = MetadataString(item.Metadata, ProtoSpecIdentity.SourceMetadataKey) ?? string.Empty;
            specs.Add(new RecordedSpec(item.TargetName, item.Category, source, hash));
        }

        return specs;
    }

    private static string? MetadataString(IReadOnlyDictionary<string, object>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            _ => null
        };
    }

    private sealed record CoverageUnit(string TargetName, string Category, string Identifier, bool IsCovered);

    private readonly record struct CoverageUnitKey(string TargetName, string Category, string Identifier);

    private sealed class CoverageUnitKeyComparer : IEqualityComparer<CoverageUnitKey>
    {
        public static readonly CoverageUnitKeyComparer Instance = new();

        public bool Equals(CoverageUnitKey left, CoverageUnitKey right)
            => string.Equals(left.TargetName, right.TargetName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Category, right.Category, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Identifier, right.Identifier, StringComparison.Ordinal);

        public int GetHashCode(CoverageUnitKey key)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.TargetName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Category),
                StringComparer.Ordinal.GetHashCode(key.Identifier));
    }

    private sealed class SummaryKeyComparer : IEqualityComparer<(string TargetName, string Category)>
    {
        public static readonly SummaryKeyComparer Instance = new();

        public bool Equals((string TargetName, string Category) left, (string TargetName, string Category) right)
            => string.Equals(left.TargetName, right.TargetName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(left.Category, right.Category, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string TargetName, string Category) key)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.TargetName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(key.Category));
    }

    private sealed record RecordedSpec(string TargetName, string Category, string Source, string Hash);
}
