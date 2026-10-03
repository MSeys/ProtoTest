namespace ProtoTest.Traces;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

/// <summary>One item of the report a sink embedded, projected as the sink wrote it.</summary>
public sealed record ProtoTraceReportItem(
    string TargetName,
    string Category,
    string Identifier,
    string Kind,
    string Status,
    int Count,
    bool? IsCovered,
    double? Value,
    string? Unit,
    string? Message,
    IReadOnlyList<string>? Tags,
    IReadOnlyDictionary<string, string?>? Metadata,
    string? DisplayName,
    string? DisplayGroup,
    IReadOnlyList<ProtoTraceReportItem>? Children);

/// <summary>A coverage unit of the report and the path that names it.</summary>
public sealed record ProtoTraceCoverageUnit(ProtoTraceReportItem Item, string Path)
{
    /// <summary>Separates a nested unit's identifier from its parent's in a path.</summary>
    public const string Separator = " › ";

    /// <summary>
    /// A child's path: its identifier under its parent's path, or the identifier alone when the collector
    /// already qualified it with the parent's (<c>Query.users</c> under <c>Query</c>). The report model in
    /// ProtoTest.Core names units by the same rule.
    /// </summary>
    public static string Combine(string? parentPath, string? parentIdentifier, string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(parentIdentifier))
        {
            return identifier;
        }

        return identifier.StartsWith(parentIdentifier, StringComparison.Ordinal)
            ? identifier
            : parentPath + Separator + identifier;
    }
}

/// <summary>The totals the report's own summary carries.</summary>
public sealed record ProtoTraceReportSummary(
    int Total,
    int TotalOccurrences,
    int CoverageTotal,
    int Covered,
    int Uncovered,
    double CoveragePercentage,
    int Warnings,
    int Errors,
    int Findings,
    int Gates,
    int Resources);

/// <summary>
/// The JSON report a reporting sink embedded as a run artifact, read as the sink wrote it: totals and
/// item verdicts come from the document, no coverage arithmetic is repeated here.
/// </summary>
public sealed class ProtoTraceReport
{
    /// <summary>The largest report read, so a tool cannot hold an arbitrarily large document in memory.</summary>
    public const long MaxReportBytes = 64L * 1024 * 1024;

    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private ProtoTraceReport(ProtoTraceArtifact artifact, ProtoTraceReportSummary summary, IReadOnlyList<ProtoTraceReportItem> items)
    {
        Artifact = artifact;
        Summary = summary;
        Items = items;
    }

    /// <summary>The artifact the report was read from.</summary>
    public ProtoTraceArtifact Artifact { get; }

    /// <summary>The report's own totals.</summary>
    public ProtoTraceReportSummary Summary { get; }

    /// <summary>The report's items in report order.</summary>
    public IReadOnlyList<ProtoTraceReportItem> Items { get; }

    /// <summary>Flattens the item tree depth first, the same order the report wrote it.</summary>
    public IEnumerable<ProtoTraceReportItem> Flatten()
    {
        foreach (var item in Items)
        {
            foreach (var entry in Flatten(item))
            {
                yield return entry;
            }
        }
    }

    /// <summary>
    /// The coverage units depth first, each with the path that names it: a nested unit's identifier under
    /// its parents' (<c>GET /a › 200 › $.id</c>), the same path run verification keys units by.
    /// </summary>
    public IEnumerable<ProtoTraceCoverageUnit> CoverageUnits()
        => Items.SelectMany(item => CoverageUnits(item, parent: null, parentPath: null));

    /// <summary>
    /// Reads the JSON report artifact the run embedded, or returns false when the archive carries none.
    /// A declared artifact that cannot be read as a report fails with the reason instead of guessing.
    /// </summary>
    public static bool TryRead(
        ProtoTraceArchive archive,
        [NotNullWhen(true)] out ProtoTraceReport? report,
        [NotNullWhen(false)] out string? reason)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var artifact = archive.Artifacts.FirstOrDefault(candidate =>
            string.Equals(candidate.MediaType, "application/json", StringComparison.OrdinalIgnoreCase));
        if (artifact is null)
        {
            report = null;
            reason = "No JSON report artifact in this run. A run embeds one only when a ProtoTest.Reporting "
                + "sink (for example JsonReportSink) exports during the run.";
            return false;
        }

        report = Read(archive, artifact);
        reason = null;
        return true;
    }

    /// <summary>Reads one declared JSON report artifact; a broken artifact fails naming the reason.</summary>
    public static ProtoTraceReport Read(ProtoTraceArchive archive, ProtoTraceArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(artifact);
        if (!string.IsNullOrEmpty(artifact.Error))
        {
            throw new InvalidDataException(
                $"The report artifact '{artifact.Name}' was not embedded: {artifact.Error}");
        }

        if (artifact.SizeBytes > MaxReportBytes)
        {
            throw new InvalidDataException(
                $"The report artifact '{artifact.Name}' is {artifact.SizeBytes} bytes; the report read caps at {MaxReportBytes} bytes.");
        }

        byte[] content;
        try
        {
            content = archive.ReadArtifact(artifact);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            throw new InvalidDataException($"Could not read the report artifact '{artifact.Name}': {exception.Message}");
        }

        if (!TryParse(content, artifact, out var report, out var parseReason))
        {
            throw new InvalidDataException(
                $"The JSON artifact '{artifact.Name}' is not a ProtoTest report: {parseReason}.");
        }

        return report;
    }

    /// <summary>Reads a JSON document as a report; a document without a summary and items is not one.</summary>
    public static bool TryParse(
        ReadOnlySpan<byte> content,
        ProtoTraceArtifact artifact,
        [NotNullWhen(true)] out ProtoTraceReport? report,
        [NotNullWhen(false)] out string? reason)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        report = null;
        if (content.Length == 0)
        {
            reason = "the artifact is empty";
            return false;
        }

        ReportDocument? document = null;
        try
        {
            document = JsonSerializer.Deserialize<ReportDocument>(content, s_options);
        }
        catch (JsonException exception)
        {
            reason = $"the artifact is not valid JSON ({exception.Message})";
            return false;
        }

        if (document?.Summary is null || document.Items is null)
        {
            reason = "the JSON document has no summary and items; it is not a ProtoReport";
            return false;
        }

        report = new ProtoTraceReport(
            artifact,
            new ProtoTraceReportSummary(
                document.Summary.Total,
                document.Summary.TotalOccurrences,
                document.Summary.CoverageTotal,
                document.Summary.Covered,
                document.Summary.Uncovered,
                document.Summary.CoveragePercentage,
                document.Summary.Warnings,
                document.Summary.Errors,
                document.Summary.Findings,
                document.Summary.Gates,
                document.Summary.Resources),
            document.Items.Select(Project).ToArray());
        reason = null;
        return true;
    }

    private static IEnumerable<ProtoTraceReportItem> Flatten(ProtoTraceReportItem item)
    {
        yield return item;
        if (item.Children is null) yield break;
        foreach (var child in item.Children)
        {
            foreach (var entry in Flatten(child))
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<ProtoTraceCoverageUnit> CoverageUnits(ProtoTraceReportItem item, ProtoTraceReportItem? parent, string? parentPath)
    {
        var path = ProtoTraceCoverageUnit.Combine(parentPath, parent?.Identifier, item.Identifier);
        if (string.Equals(item.Kind, "coverage", StringComparison.OrdinalIgnoreCase) && item.IsCovered is not null)
        {
            yield return new ProtoTraceCoverageUnit(item, path);
        }

        foreach (var child in item.Children ?? [])
        {
            foreach (var unit in CoverageUnits(child, item, path))
            {
                yield return unit;
            }
        }
    }

    private static ProtoTraceReportItem Project(ReportItem item) => new(
        item.TargetName ?? string.Empty,
        item.Category ?? string.Empty,
        item.Identifier ?? string.Empty,
        item.Kind ?? string.Empty,
        item.Status ?? string.Empty,
        item.Count,
        item.IsCovered,
        item.Value,
        item.Unit,
        item.Message,
        item.Tags,
        item.Metadata?.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.ValueKind == JsonValueKind.Null ? null : pair.Value.ToString(),
            StringComparer.Ordinal),
        item.DisplayName,
        item.DisplayGroup,
        item.Children?.Select(Project).ToArray());

    /// <summary>The report document as JSON carries it; missing members stay null so a foreign JSON file is refused with a reason.</summary>
    private sealed record ReportDocument
    {
        public ReportSummary? Summary { get; init; }

        public IReadOnlyList<ReportItem>? Items { get; init; }
    }

    private sealed record ReportSummary
    {
        public int Total { get; init; }

        public int TotalOccurrences { get; init; }

        public int CoverageTotal { get; init; }

        public int Covered { get; init; }

        public int Uncovered { get; init; }

        public double CoveragePercentage { get; init; }

        public int Warnings { get; init; }

        public int Errors { get; init; }

        public int Findings { get; init; }

        public int Gates { get; init; }

        public int Resources { get; init; }
    }

    private sealed record ReportItem
    {
        public string? TargetName { get; init; }

        public string? Category { get; init; }

        public string? Identifier { get; init; }

        public string? Kind { get; init; }

        public string? Status { get; init; }

        public int Count { get; init; }

        public bool? IsCovered { get; init; }

        public double? Value { get; init; }

        public string? Unit { get; init; }

        public string? Message { get; init; }

        public IReadOnlyList<string>? Tags { get; init; }

        public IReadOnlyList<ReportItem>? Children { get; init; }

        public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }

        public string? DisplayName { get; init; }

        public string? DisplayGroup { get; init; }
    }
}
