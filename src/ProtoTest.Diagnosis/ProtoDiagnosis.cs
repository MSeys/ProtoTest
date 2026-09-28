namespace ProtoTest.Diagnosis;

using System.Globalization;
using System.Text;
using ProtoTest.Traces;

/// <summary>
/// Reads one run's evidence and states the deterministic causal summary: which tests failed and why,
/// what the failing operation carried and changed, and what the report published. There is no
/// inference, no network and no LLM; evidence that was not recorded is stated as absent with the
/// reason instead of reconstructed.
/// </summary>
public static class ProtoDiagnosis
{
    /// <summary>The largest number of recorded mismatches one failure entry carries.</summary>
    public const int MaxMismatches = 25;

    /// <summary>The preview length of a section value, detail or content.</summary>
    public const int MaxPreviewCharacters = 4096;

    /// <summary>The largest recorded error message one failure entry carries.</summary>
    public const int MaxErrorMessageCharacters = 4000;

    /// <summary>The largest ancestor chain the context package carries.</summary>
    public const int MaxAncestors = 32;

    /// <summary>The largest number of operation sections the context package carries.</summary>
    public const int MaxSections = 16;

    /// <summary>The largest number of items one section preview carries.</summary>
    public const int MaxSectionItems = 50;

    /// <summary>The largest number of operation attributes the context package carries.</summary>
    public const int MaxAttributes = 50;

    /// <summary>The largest number of source lines one snippet carries.</summary>
    public const int MaxSnippetLines = 41;

    /// <summary>The largest number of test artifacts one entry or context carries.</summary>
    public const int MaxArtifacts = 10;

    /// <summary>The largest artifact content preview, in bytes.</summary>
    public const int MaxArtifactPreviewBytes = 64 * 1024;

    /// <summary>The largest number of state items the context package carries.</summary>
    public const int MaxStateItems = 10;

    /// <summary>The largest number of changes one state item carries.</summary>
    public const int MaxStateChangesPerItem = 16;

    /// <summary>The largest number of findings one document or context carries.</summary>
    public const int MaxFindings = 10;

    /// <summary>The largest number of run gates one document or context carries.</summary>
    public const int MaxGates = 10;

    /// <summary>The largest number of coverage rows the context package carries.</summary>
    public const int MaxCoverageRows = 10;

    private static readonly string[] s_textMediaTypeFragments =
        ["json", "text", "xml", "yaml", "yml", "csv", "html"];

    /// <summary>Reads one run's diagnosis from a trace file.</summary>
    public static ProtoDiagnosisDocument Read(string tracePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tracePath);
        return Read(ProtoTraceArchive.Open(tracePath));
    }

    /// <summary>Reads one run's diagnosis from an opened archive.</summary>
    public static ProtoDiagnosisDocument Read(ProtoTraceArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var report = TryReport(archive, out var reportAbsentReason);
        var (gates, gatesTruncated) = ReadGates(archive, report);
        var (findings, findingsTruncated, findingsByTest) = ReadFindings(archive, report);

        var failures = archive.Tests
            .Where(test => !test.Succeeded)
            .Select(test => DiagnoseTest(archive, test, FindingsFor(findingsByTest, test), includeArtifactContent: false))
            .ToArray();

        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in archive.RunAttributes)
        {
            if (key.StartsWith("environment.", StringComparison.Ordinal))
            {
                environment[key["environment.".Length..]] = value;
            }
        }

        var outcomes = archive.Tests
            .GroupBy(test => test.Outcome, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return new ProtoDiagnosisDocument(
            ProtoDiagnosisDocument.CurrentDigestVersion,
            archive.FormatVersion,
            archive.RunId,
            archive.SourcePath,
            archive.RunStartedAtUtc,
            archive.RunCompletedAtUtc,
            environment,
            outcomes,
            failures,
            gates,
            gatesTruncated,
            findings,
            findingsTruncated,
            Coverage(report),
            reportAbsentReason);
    }

    /// <summary>
    /// Reads what an agent receives for one test: the failure entry with its context package. The test
    /// must exist in the run; a green test gets a context without a failure.
    /// </summary>
    public static ProtoDiagnosisContext ReadContext(
        ProtoTraceArchive archive,
        string testId,
        bool includeArtifactContent = false)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(testId);
        var test = archive.Tests.FirstOrDefault(candidate => string.Equals(candidate.TestId, testId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"No test '{testId}' in run '{archive.RunId}'.");

        var report = TryReport(archive, out var reportAbsentReason);
        var (_, _, findingsByTest) = ReadFindings(archive, report);
        var testFindings = FindingsFor(findingsByTest, test);
        var entry = DiagnoseTest(archive, test, testFindings, includeArtifactContent);
        var failure = test.Failure;
        var failureProjection = failure is null ? null : ProjectOperation(failure, includeAttributes: true, out _);
        var call = failure is null ? null : test.CallAncestor(failure);

        var ancestors = failure is null
            ? (IReadOnlyList<ProtoTraceOperation>)[]
            : test.Ancestors(failure);
        var (ancestorPage, ancestorsTruncated) = Page(ancestors, MaxAncestors);
        var (sections, sectionsTruncated) = ReadSections(failure);
        var (source, sourceAbsentReason) = ReadSource(archive, failure);
        var (state, stateTruncated, stateAbsentReason) = ReadState(archive, test, failure);
        var (coverageRows, coverageRowsTruncated) = ReadCoverageRows(report, failure, ancestors);
        var (contextFindings, findingsTruncated) = Page(testFindings, MaxFindings);
        var (contextGates, gatesTruncated) = ReadGates(archive, report);

        return new ProtoDiagnosisContext(
            ProtoDiagnosisDocument.CurrentDigestVersion,
            archive.RunId,
            archive.SourcePath,
            entry.TestId,
            entry.Name,
            entry.ClassName,
            entry.MethodName,
            entry.Outcome,
            entry.Rule,
            entry.UnexplainedReason,
            failureProjection,
            entry.Mismatches,
            entry.MismatchesTruncated,
            entry.Findings,
            entry.FindingsTruncated,
            ancestorPage.Select(operation => ProjectOperation(operation, includeAttributes: false, includeErrorMessage: false, out _)).ToArray(),
            ancestorsTruncated,
            call is null ? null : ProjectOperation(call, includeAttributes: false, includeErrorMessage: false, out _),
            sections,
            sectionsTruncated,
            source,
            sourceAbsentReason,
            entry.Artifacts,
            entry.ArtifactsTruncated,
            state,
            stateTruncated,
            stateAbsentReason,
            new ProtoDiagnosisReportContext(
                Coverage(report),
                coverageRows,
                coverageRowsTruncated,
                contextFindings,
                findingsTruncated,
                contextGates,
                gatesTruncated,
                reportAbsentReason));
    }

    private static ProtoDiagnosedTest DiagnoseTest(
        ProtoTraceArchive archive,
        ProtoTraceTest test,
        IReadOnlyList<ProtoDiagnosisFinding> findings,
        bool includeArtifactContent)
    {
        var failure = test.Failure;
        var mismatches = failure is null ? [] : ProtoTraceOperation.ReadMismatches(failure);
        var (mismatchPage, mismatchesTruncated) = Page(mismatches, MaxMismatches);
        var (findingPage, findingsTruncated) = Page(findings, MaxFindings);
        var (artifacts, artifactsTruncated) = ReadArtifacts(archive, test, includeArtifactContent);
        var (rule, unexplainedReason) = Explain(test.Outcome, failure, mismatches, findings);

        return new ProtoDiagnosedTest(
            test.TestId,
            test.Name,
            test.ClassName,
            test.MethodName,
            test.Outcome,
            test.DurationMs,
            failure is null ? null : ProjectOperation(failure, includeAttributes: false, out _),
            rule,
            unexplainedReason,
            mismatchPage,
            mismatchesTruncated,
            findingPage,
            findingsTruncated,
            artifacts,
            artifactsTruncated);
    }

    /// <summary>
    /// The five "explained" rules, in order: a failed assertion with recorded checks or mismatches, a
    /// failed operation with an error, a runner-reported failure, a finding on a test the runner did
    /// not fail, and (run level) a failed gate, which the document reports in its gates list.
    /// </summary>
    private static (ProtoDiagnosisRule? Rule, string? Reason) Explain(
        string outcome,
        ProtoTraceOperation? failure,
        IReadOnlyList<ProtoTraceMismatch> mismatches,
        IReadOnlyList<ProtoDiagnosisFinding> findings)
    {
        if (failure is not null)
        {
            if (failure.Failed
                && failure.Kind.StartsWith("assert.", StringComparison.Ordinal)
                && (mismatches.Count > 0 || ErrorCheck(failure) is not null))
            {
                return (ProtoDiagnosisRule.Assertion, null);
            }

            if (failure.HasError)
            {
                return (
                    failure.Kind.StartsWith("test.", StringComparison.Ordinal)
                        ? ProtoDiagnosisRule.RunnerFailure
                        : ProtoDiagnosisRule.OperationError,
                    null);
            }
        }

        if (findings.Count > 0)
        {
            return (ProtoDiagnosisRule.Finding, null);
        }

        if (string.Equals(outcome, "skipped", StringComparison.Ordinal))
        {
            return (null, "The test was skipped; there is no failure to explain.");
        }

        return (null, failure is null
            ? "No failed operation was recorded and no finding explains the outcome; open the trace in the viewer."
            : $"The selected failure '{failure.Kind} {failure.Name}' recorded no error or check to explain it; open the trace in the viewer.");
    }

    private static ProtoTraceSectionItem? ErrorCheck(ProtoTraceOperation operation)
    {
        foreach (var section in operation.Sections)
        {
            if (!string.Equals(section.Kind, "checks", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var item in section.Items)
            {
                if (string.Equals(item.Tone, "error", StringComparison.Ordinal))
                {
                    return item;
                }
            }
        }

        return null;
    }

    private static ProtoTraceReport? TryReport(ProtoTraceArchive archive, out string? absentReason)
    {
        try
        {
            if (ProtoTraceReport.TryRead(archive, out var report, out var absence))
            {
                absentReason = null;
                return report;
            }

            absentReason = absence;
            return null;
        }
        catch (InvalidDataException exception)
        {
            absentReason = $"The report artifact could not be read: {exception.Message}";
            return null;
        }
    }

    private static ProtoDiagnosisCoverage? Coverage(ProtoTraceReport? report)
        => report is null
            ? null
            : new ProtoDiagnosisCoverage(
                report.Summary.CoverageTotal,
                report.Summary.Covered,
                report.Summary.Uncovered,
                report.Summary.CoveragePercentage,
                report.Artifact.Name,
                report.Artifact.ArchivePath,
                report.Artifact.SizeBytes);

    private static (IReadOnlyList<ProtoDiagnosisGate> Gates, bool Truncated) ReadGates(
        ProtoTraceArchive archive,
        ProtoTraceReport? report)
    {
        if (report is not null)
        {
            return Page(
                report.Flatten()
                    .Where(item => string.Equals(item.Kind, "gate", StringComparison.OrdinalIgnoreCase))
                    .Select(ProjectGate)
                    .ToArray(),
                MaxGates);
        }

        return Page(
            archive.RunMoments
                .Where(moment => string.Equals(moment.Kind, "gate.evaluate", StringComparison.Ordinal))
                .Select(ProjectGate)
                .ToArray(),
            MaxGates);
    }

    private static ProtoDiagnosisGate ProjectGate(ProtoTraceReportItem item)
        => new(
            item.Identifier,
            GateVerdict(item.Status),
            item.Message,
            item.Tags ?? []);

    private static ProtoDiagnosisGate ProjectGate(ProtoTraceMoment moment)
        => new(
            Attribute(moment, "gate.name") ?? moment.Name,
            Attribute(moment, "gate.status") is { Length: > 0 } status ? status.ToLowerInvariant() : GateVerdict(moment.Outcome),
            Attribute(moment, "gate.message"),
            Attribute(moment, "gate.details") is { Length: > 0 } details ? [details] : []);

    private static (IReadOnlyList<ProtoDiagnosisFinding> Findings, bool Truncated, Dictionary<string, List<ProtoDiagnosisFinding>> ByTest) ReadFindings(
        ProtoTraceArchive archive,
        ProtoTraceReport? report)
    {
        var byTest = new Dictionary<string, List<ProtoDiagnosisFinding>>(StringComparer.Ordinal);
        if (report is not null)
        {
            var projected = report.Flatten()
                .Where(item => string.Equals(item.Kind, "finding", StringComparison.OrdinalIgnoreCase))
                .Select(ProjectFinding)
                .ToArray();
            foreach (var finding in projected)
            {
                if (finding.TestId is not { Length: > 0 } testId)
                {
                    continue;
                }

                if (!byTest.TryGetValue(testId, out var list))
                {
                    byTest[testId] = list = [];
                }

                list.Add(finding);
            }

            var (findingPage, truncated) = Page(projected, MaxFindings);
            return (findingPage, truncated, byTest);
        }

        var all = new List<ProtoDiagnosisFinding>();
        foreach (var test in archive.Tests)
        {
            var perTest = ReadFindings(test).ToArray();
            byTest[test.TestId] = [.. perTest];
            all.AddRange(perTest);
        }

        all.AddRange(archive.RunEvidence
            .Where(evidence => string.Equals(evidence.Record, "finding", StringComparison.Ordinal))
            .Select(evidence => ProjectFinding(null, evidence)));
        var (allFindings, allTruncated) = Page(all, MaxFindings);
        return (allFindings, allTruncated, byTest);
    }

    private static IEnumerable<ProtoDiagnosisFinding> ReadFindings(ProtoTraceTest test)
    {
        foreach (var operation in test.Operations)
        {
            foreach (var evidence in operation.Evidence)
            {
                if (string.Equals(evidence.Record, "finding", StringComparison.Ordinal))
                {
                    yield return ProjectFinding(test, evidence);
                }
            }
        }

        foreach (var evidence in test.Evidence)
        {
            if (string.Equals(evidence.Record, "finding", StringComparison.Ordinal))
            {
                yield return ProjectFinding(test, evidence);
            }
        }
    }

    private static ProtoDiagnosisFinding ProjectFinding(ProtoTraceReportItem item)
        => new(
            item.Message ?? item.Identifier,
            item.Status.ToLowerInvariant(),
            item.Category,
            item.TargetName,
            item.Tags ?? [],
            Metadata(item, "test.id"),
            Metadata(item, "test.name"));

    private static ProtoDiagnosisFinding ProjectFinding(ProtoTraceTest? test, ProtoTraceRecordEvent evidence)
        => new(
            evidence.Name,
            (evidence.Status ?? "unknown").ToLowerInvariant(),
            evidence.Category ?? string.Empty,
            evidence.TargetName,
            evidence.Tags,
            test?.TestId ?? Metadata(evidence, "test.id"),
            test?.Name ?? Metadata(evidence, "test.name"));

    private static IReadOnlyList<ProtoDiagnosisFinding> FindingsFor(
        Dictionary<string, List<ProtoDiagnosisFinding>> byTest,
        ProtoTraceTest test)
        => byTest.TryGetValue(test.TestId, out var findings) ? findings : [];

    /// <summary>The report status and the gate outcome tokens both read as one verdict vocabulary.</summary>
    private static string GateVerdict(string token)
        => token.ToLowerInvariant() switch
        {
            "success" => "passed",
            "passed" => "passed",
            "warning" => "warning",
            "error" => "failed",
            "failed" => "failed",
            "skipped" => "skipped",
            "neutral" => "skipped",
            "succeeded" => "passed",
            "partial" => "warning",
            "cancelled" => "failed",
            "info" => "info",
            _ => token.ToLowerInvariant()
        };

    private static ProtoDiagnosisOperation ProjectOperation(
        ProtoTraceOperation operation,
        bool includeAttributes,
        out bool attributesTruncated)
        => ProjectOperation(operation, includeAttributes, includeErrorMessage: true, out attributesTruncated);

    private static ProtoDiagnosisOperation ProjectOperation(
        ProtoTraceOperation operation,
        bool includeAttributes,
        bool includeErrorMessage,
        out bool attributesTruncated)
    {
        attributesTruncated = false;
        IReadOnlyDictionary<string, string?>? attributes = null;
        if (includeAttributes)
        {
            var preview = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var (key, value) in operation.Attributes)
            {
                if (preview.Count >= MaxAttributes)
                {
                    attributesTruncated = true;
                    break;
                }

                preview[key] = Preview(value);
            }

            attributes = preview;
        }

        return new ProtoDiagnosisOperation(
            operation.SpanId,
            operation.ParentSpanId,
            operation.Kind,
            operation.Name,
            operation.Source,
            operation.Phase,
            operation.Status,
            operation.ErrorType,
            includeErrorMessage ? Truncate(operation.ErrorMessage) : null,
            operation.SourceFile,
            operation.SourceLine,
            operation.SourceFunction,
            operation.EntityKind,
            operation.EntityId,
            operation.Subject,
            attributes,
            attributesTruncated);
    }

    private static (IReadOnlyList<ProtoDiagnosisArtifact> Artifacts, bool Truncated) ReadArtifacts(
        ProtoTraceArchive archive,
        ProtoTraceTest test,
        bool includeContent)
    {
        // An artifact is reachable from the operation its attachment event names; an artifact with no
        // attachment event is declared at the test level.
        var operationByArtifact = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var operation in test.Operations)
        {
            foreach (var evidence in operation.Evidence)
            {
                if (string.Equals(evidence.Record, "attachment", StringComparison.Ordinal)
                    && evidence.ArtifactId is { Length: > 0 } artifactId)
                {
                    operationByArtifact.TryAdd(artifactId, operation.SpanId);
                }
            }
        }

        foreach (var evidence in test.Evidence)
        {
            if (string.Equals(evidence.Record, "attachment", StringComparison.Ordinal)
                && evidence.ArtifactId is { Length: > 0 } artifactId)
            {
                operationByArtifact.TryAdd(artifactId, null);
            }
        }

        return Page(
            test.Artifacts
                .Select(artifact => ProjectArtifact(
                    archive,
                    artifact,
                    operationByArtifact.GetValueOrDefault(artifact.Id),
                    includeContent))
                .ToArray(),
            MaxArtifacts);
    }

    private static ProtoDiagnosisArtifact ProjectArtifact(
        ProtoTraceArchive archive,
        ProtoTraceArtifact artifact,
        string? operationId,
        bool includeContent)
    {
        if (!includeContent)
        {
            return new ProtoDiagnosisArtifact(
                artifact.Id, artifact.Name, artifact.MediaType, artifact.Description, artifact.ArchivePath,
                artifact.SizeBytes, artifact.Error, operationId, null, false, null);
        }

        if (artifact.Error is not null)
        {
            return new ProtoDiagnosisArtifact(
                artifact.Id, artifact.Name, artifact.MediaType, artifact.Description, artifact.ArchivePath,
                artifact.SizeBytes, artifact.Error, operationId, null, false, $"not embedded: {artifact.Error}");
        }

        if (!IsTextMediaType(artifact.MediaType))
        {
            return new ProtoDiagnosisArtifact(
                artifact.Id, artifact.Name, artifact.MediaType, artifact.Description, artifact.ArchivePath,
                artifact.SizeBytes, artifact.Error, operationId, null, false,
                artifact.SizeBytes is { } size
                    ? $"binary content ({size.ToString(CultureInfo.InvariantCulture)} bytes); read the archive entry"
                    : "binary content; read the archive entry");
        }

        try
        {
            var content = archive.ReadArtifact(artifact);
            var truncated = content.Length > MaxArtifactPreviewBytes;
            return new ProtoDiagnosisArtifact(
                artifact.Id, artifact.Name, artifact.MediaType, artifact.Description, artifact.ArchivePath,
                artifact.SizeBytes, artifact.Error, operationId,
                Encoding.UTF8.GetString(truncated ? content.AsSpan(0, MaxArtifactPreviewBytes) : content),
                truncated,
                null);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return new ProtoDiagnosisArtifact(
                artifact.Id, artifact.Name, artifact.MediaType, artifact.Description, artifact.ArchivePath,
                artifact.SizeBytes, artifact.Error, operationId, null, false, $"could not read: {exception.Message}");
        }
    }

    private static (IReadOnlyList<ProtoDiagnosisSection> Sections, bool Truncated) ReadSections(ProtoTraceOperation? failure)
    {
        if (failure is null)
        {
            return ([], false);
        }

        return Page(
            failure.Sections.Select(ProjectSection).ToArray(),
            MaxSections);
    }

    private static ProtoDiagnosisSection ProjectSection(ProtoTraceSection section)
        => new(
            section.Label,
            section.Kind,
            section.Items
                .Take(MaxSectionItems)
                .Select(item => new ProtoDiagnosisSectionItem(
                    item.Label,
                    Preview(item.Value),
                    Preview(item.Detail),
                    item.Tone))
                .ToArray(),
            section.Items.Count > MaxSectionItems,
            Preview(section.Content, out var contentTruncated),
            section.Language,
            contentTruncated);

    private static (ProtoDiagnosisSource? Source, string? Reason) ReadSource(
        ProtoTraceArchive archive,
        ProtoTraceOperation? failure)
    {
        if (failure?.SourceFile is not { Length: > 0 } file)
        {
            return (null, "The trace recorded no source location for the failing operation.");
        }

        if (failure.SourceLine is not { } line || line <= 0)
        {
            return (null, $"The trace recorded no line for '{file}'.");
        }

        string? text;
        try
        {
            text = archive.ReadSource(file);
        }
        catch (InvalidOperationException exception)
        {
            return (null, exception.Message);
        }

        if (text is null)
        {
            return (null, $"The archive does not embed '{file}'; EmbedSources is off or the file exceeded the 512 KB embed cap.");
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (line > lines.Length)
        {
            return (null, $"The recorded line {line} is outside '{file}' ({lines.Length} lines).");
        }

        var half = MaxSnippetLines / 2;
        var start = Math.Max(1, line - half);
        var end = Math.Min(lines.Length, line + half);
        var snippet = new List<ProtoDiagnosisSourceLine>();
        for (var number = start; number <= end; number++)
        {
            snippet.Add(new ProtoDiagnosisSourceLine(number, lines[number - 1]));
        }

        return (new ProtoDiagnosisSource(file, line, start, end, snippet), null);
    }

    private static (IReadOnlyList<ProtoDiagnosisStateItem> Items, bool Truncated, string? AbsentReason) ReadState(
        ProtoTraceArchive archive,
        ProtoTraceTest test,
        ProtoTraceOperation? failure)
    {
        if (failure is null)
        {
            return ([], false, null);
        }

        ProtoTraceState state;
        try
        {
            state = archive.ReadState();
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
        {
            return ([], false, $"The state document could not be read: {exception.Message}");
        }

        var items = new List<ProtoDiagnosisStateItem>();
        var truncated = false;
        foreach (var item in state.RunItems.Concat(state.ItemsFor(test.TestId)))
        {
            var caused = item.Changes
                .Where(change => string.Equals(change.OperationId, failure.SpanId, StringComparison.Ordinal))
                .ToArray();
            var actedOn = failure.EntityKind is { Length: > 0 } kind
                && failure.EntityId is { Length: > 0 } id
                && string.Equals(item.Kind, kind, StringComparison.Ordinal)
                && string.Equals(item.Id, id, StringComparison.Ordinal);
            if (caused.Length == 0 && !actedOn)
            {
                continue;
            }

            if (items.Count >= MaxStateItems)
            {
                truncated = true;
                break;
            }

            var (changes, changesTruncated) = Page(caused, MaxStateChangesPerItem);
            items.Add(new ProtoDiagnosisStateItem(
                item.Kind,
                item.Id,
                item.Name,
                item.State,
                changes
                    .Select(change => new ProtoDiagnosisStateChange(
                        change.Change, change.AtUtc, change.Source, change.Inferred, change.State))
                    .ToArray(),
                changesTruncated));
        }

        return (items, truncated, null);
    }

    private static (IReadOnlyList<ProtoDiagnosisCoverageRow> Rows, bool Truncated) ReadCoverageRows(
        ProtoTraceReport? report,
        ProtoTraceOperation? failure,
        IReadOnlyList<ProtoTraceOperation> ancestors)
    {
        if (report is null || failure is null)
        {
            return ([], false);
        }

        var subjects = new List<string>();
        if (failure.Subject is { Length: > 0 } own)
        {
            subjects.Add(own);
        }

        foreach (var ancestor in ancestors)
        {
            if (ancestor.Subject is { Length: > 0 } subject && !subjects.Contains(subject, StringComparer.Ordinal))
            {
                subjects.Add(subject);
            }
        }

        if (subjects.Count == 0)
        {
            return ([], false);
        }

        return Page(
            report.Flatten()
                .Where(item => string.Equals(item.Kind, "coverage", StringComparison.OrdinalIgnoreCase)
                    && item.IsCovered is not null
                    && subjects.Contains(item.Identifier, StringComparer.Ordinal))
                .Select(item => new ProtoDiagnosisCoverageRow(
                    item.TargetName,
                    item.Category,
                    item.Identifier,
                    item.Status.ToLowerInvariant(),
                    item.Count,
                    item.IsCovered!.Value,
                    item.Message,
                    item.DisplayName))
                .ToArray(),
            MaxCoverageRows);
    }

    private static (IReadOnlyList<T> Items, bool Truncated) Page<T>(IReadOnlyList<T> items, int max)
        => items.Count > max
            ? (items.Take(max).ToArray(), true)
            : (items, false);

    private static bool IsTextMediaType(string mediaType)
    {
        foreach (var fragment in s_textMediaTypeFragments)
        {
            if (mediaType.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Preview(string? value) => Preview(value, out _);

    private static string? Preview(string? value, out bool truncated)
    {
        truncated = false;
        if (value is null || value.Length <= MaxPreviewCharacters)
        {
            return value;
        }

        truncated = true;
        return string.Concat(
            value.AsSpan(0, MaxPreviewCharacters),
            $"... (truncated; {value.Length} characters)");
    }

    private static string? Truncate(string? message)
        => message is null || message.Length <= MaxErrorMessageCharacters
            ? message
            : string.Concat(
                message.AsSpan(0, MaxErrorMessageCharacters),
                $"... (truncated; {message.Length} characters)");

    private static string? Attribute(ProtoTraceMoment moment, string name)
        => moment.Attributes.TryGetValue(name, out var value) ? value : null;

    private static string? Metadata(ProtoTraceReportItem item, string name)
        => item.Metadata is not null && item.Metadata.TryGetValue(name, out var value) ? value : null;

    private static string? Metadata(ProtoTraceRecordEvent evidence, string name)
        => evidence.Metadata.TryGetValue(name, out var value) ? value : null;
}
