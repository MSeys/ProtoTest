namespace ProtoTest.Traces;

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

/// <summary>One operation in a test's timeline, as the archive records it.</summary>
public sealed record ProtoTraceOperation(
    string SpanId,
    string? ParentSpanId,
    string Name,
    string Kind,
    string Source,
    string Phase,
    string Status,
    string? ErrorType,
    string? ErrorMessage,
    DateTimeOffset StartedAtUtc,
    double? DurationMs,
    string? EntityKind,
    string? EntityId,
    IReadOnlyDictionary<string, string?> Attributes)
{
    /// <summary>The subject attributes the producers record, in the order a reader should prefer them.</summary>
    public static IReadOnlyList<string> SubjectAttributes { get; } =
        ["request.identifier", "graphql.operation", "messaging.destination", "sheets.range"];

    /// <summary>The source file the operation started in, when the trace captured locations.</summary>
    public string? SourceFile => Attribute("code.file.path");

    /// <summary>The source line the operation started on, when the trace captured locations.</summary>
    public int? SourceLine
        => int.TryParse(Attribute("code.line.number"), CultureInfo.InvariantCulture, out var line) ? line : null;

    /// <summary>The source function the operation started in, when the trace captured locations.</summary>
    public string? SourceFunction => Attribute("code.function.name");

    /// <summary>The subject the operation is about: a request, a GraphQL operation, a destination or a range.</summary>
    public string? Subject
    {
        get
        {
            foreach (var name in SubjectAttributes)
            {
                if (Attribute(name) is { Length: > 0 } value)
                {
                    return value;
                }
            }

            return null;
        }
    }

    /// <summary>Whether this operation recorded a failure status.</summary>
    public bool Failed => string.Equals(Status, "failed", StringComparison.Ordinal);

    /// <summary>Whether the wire carried an error object, even one without a type.</summary>
    public bool HasError { get; init; }

    /// <summary>The sections the operation recorded: facts, payloads, checks and diffs.</summary>
    public IReadOnlyList<ProtoTraceSection> Sections { get; init; } = [];

    /// <summary>The moments recorded inside the operation: things that happened at a time.</summary>
    public IReadOnlyList<ProtoTraceMoment> Moments { get; init; } = [];

    /// <summary>The observations, attachments and findings recorded on the operation.</summary>
    public IReadOnlyList<ProtoTraceRecordEvent> Evidence { get; init; } = [];

    /// <summary>Whether the operation is a protocol call a failure belongs to.</summary>
    public static bool IsCall(string kind)
        => kind is "http.request" or "graphql.operation" or "grpc.call"
        || kind.StartsWith("messaging.", StringComparison.Ordinal);

    /// <summary>
    /// The shape mismatches the operation recorded: the <c>shape.mismatches</c> attribute when a shape
    /// assertion wrote one, otherwise the items of its diff sections. Empty when neither is recorded.
    /// </summary>
    public static IReadOnlyList<ProtoTraceMismatch> ReadMismatches(ProtoTraceOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Attributes.TryGetValue("shape.mismatches", out var json) && !string.IsNullOrWhiteSpace(json))
        {
            var recorded = ReadRecordedMismatches(json);
            if (recorded.Count > 0)
            {
                return recorded;
            }
        }

        var mismatches = new List<ProtoTraceMismatch>();
        foreach (var section in operation.Sections)
        {
            if (!string.Equals(section.Kind, "diff", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var item in section.Items)
            {
                mismatches.Add(new ProtoTraceMismatch(
                    item.Label,
                    null,
                    Json(item.Value),
                    Json(item.Detail)));
            }
        }

        return mismatches;
    }

    private static List<ProtoTraceMismatch> ReadRecordedMismatches(string json)
    {
        var mismatches = new List<ProtoTraceMismatch>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return mismatches;
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                mismatches.Add(new ProtoTraceMismatch(
                    GetString(element, "propertyPath") ?? string.Empty,
                    GetString(element, "reason"),
                    element.TryGetProperty("expected", out var expected) ? expected.Clone() : null,
                    element.TryGetProperty("actual", out var actual) ? actual.Clone() : null));
            }
        }
        catch (JsonException)
        {
        }

        return mismatches;
    }

    private static JsonElement? Json(string? value)
        => value is null ? null : JsonSerializer.SerializeToElement(value);

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ToString()
            : null;

    private string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;
}

/// <summary>
/// One recorded shape mismatch: the property path, the recorded reason, and the expected and actual
/// values as JSON. A diff section carries its values as strings, so both sides read text.
/// </summary>
public sealed record ProtoTraceMismatch(
    string Path,
    string? Reason,
    JsonElement? Expected,
    JsonElement? Actual);

/// <summary>One file the archive declares, on the run or on a test, with where it lives in the archive.</summary>
public sealed record ProtoTraceArtifact(
    string Id,
    string Name,
    string MediaType,
    string? Description,
    string ArchivePath,
    long? SizeBytes,
    string? Error);

/// <summary>One test in the run, with its operations in recorded order.</summary>
public sealed record ProtoTraceTest(
    string TestId,
    string Name,
    string? ClassName,
    string MethodName,
    string Outcome,
    double DurationMs,
    IReadOnlyList<ProtoTraceOperation> Operations)
{
    /// <summary>The artifacts the test declared; the content lives behind <see cref="ProtoTraceArchive.ReadArtifact(ProtoTraceArtifact)"/>.</summary>
    public IReadOnlyList<ProtoTraceArtifact> Artifacts { get; init; } = [];

    /// <summary>Moments the test recorded outside any operation.</summary>
    public IReadOnlyList<ProtoTraceMoment> Moments { get; init; } = [];

    /// <summary>Observations, attachments and findings the test recorded outside any operation.</summary>
    public IReadOnlyList<ProtoTraceRecordEvent> Evidence { get; init; } = [];

    /// <summary>Whether the test recorded a full success.</summary>
    public bool Succeeded => string.Equals(Outcome, "succeeded", StringComparison.Ordinal);

    /// <summary>
    /// The operation that best explains a non-succeeded test: the deepest failing operation, letting an
    /// <c>assert.*</c> check outrank anything with an error and ranking phase spans (<c>test.*</c>)
    /// last. A cancelled operation that recorded an error never outranks a failed one; it is selected
    /// only when nothing failed. The viewer applies the same rule, so the CLI, the tools and the viewer
    /// select one failure.
    /// </summary>
    public ProtoTraceOperation? Failure
    {
        get
        {
            var byId = BySpanId();
            ProtoTraceOperation? best = null;
            var bestTier = int.MinValue;
            var bestScore = int.MinValue;
            foreach (var operation in Operations)
            {
                if (!operation.Failed && !operation.HasError)
                {
                    continue;
                }

                // Failures outrank cancellations whatever their depth: a cancelled child is usually a
                // consequence of the failure above it, so depth only decides inside one tier.
                var tier = operation.Failed ? 1 : 0;
                var score = Depth(operation, byId) * 10
                    + (operation.HasError ? 100 : 0)
                    + (operation.Kind.StartsWith("assert.", StringComparison.Ordinal) ? 1000 : 0)
                    + (operation.Kind.StartsWith("test.", StringComparison.Ordinal) ? -500 : 0);
                if (best is null || tier > bestTier || (tier == bestTier && score > bestScore))
                {
                    best = operation;
                    bestTier = tier;
                    bestScore = score;
                }
            }

            return best;
        }
    }

    /// <summary>The operation's parent, when the test recorded one.</summary>
    public ProtoTraceOperation? Parent(ProtoTraceOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation.ParentSpanId is { Length: > 0 } parentId
            && BySpanId().TryGetValue(parentId, out var parent)
            && !ReferenceEquals(parent, operation)
                ? parent
                : null;
    }

    /// <summary>The operation's ancestors, nearest first, up to the phase operation that opened the test.</summary>
    public IReadOnlyList<ProtoTraceOperation> Ancestors(ProtoTraceOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var byId = BySpanId();
        var ancestors = new List<ProtoTraceOperation>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { operation.SpanId };
        var current = operation;
        while (current.ParentSpanId is { Length: > 0 } parentId
            && byId.TryGetValue(parentId, out var parent)
            && !ReferenceEquals(parent, current)
            && seen.Add(parent.SpanId))
        {
            ancestors.Add(parent);
            current = parent;
        }

        return ancestors;
    }

    /// <summary>The nearest ancestor that is a protocol call, the operation itself included.</summary>
    public ProtoTraceOperation? CallAncestor(ProtoTraceOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var byId = BySpanId();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = operation;
        while (current is not null && seen.Add(current.SpanId))
        {
            if (ProtoTraceOperation.IsCall(current.Kind))
            {
                return current;
            }

            current = current.ParentSpanId is { Length: > 0 } parentId
                && byId.TryGetValue(parentId, out var parent)
                && !ReferenceEquals(parent, current)
                    ? parent
                    : null;
        }

        return null;
    }

    private Dictionary<string, ProtoTraceOperation> BySpanId()
    {
        var byId = new Dictionary<string, ProtoTraceOperation>(Operations.Count, StringComparer.Ordinal);
        foreach (var operation in Operations)
        {
            byId[operation.SpanId] = operation;
        }

        return byId;
    }

    private static int Depth(ProtoTraceOperation operation, Dictionary<string, ProtoTraceOperation> byId)
    {
        var depth = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal) { operation.SpanId };
        var current = operation;
        while (current.ParentSpanId is { Length: > 0 } parentId
            && byId.TryGetValue(parentId, out var parent)
            && !ReferenceEquals(parent, current)
            && seen.Add(parent.SpanId))
        {
            depth++;
            current = parent;
        }

        return depth;
    }
}

/// <summary>
/// A read-only view of one `.prototrace` archive: the run's identity and environment, its tests and
/// their operations, sections, moments, evidence and artifacts, and (on request) the tracked state.
/// This reader depends on nothing but the wire contract, so tools and agents can consume traces
/// without taking a dependency on ProtoTest.Core.
/// </summary>
public sealed class ProtoTraceArchive
{
    private const string ManifestEntryName = "manifest.json";
    private const string DefaultSpansEntryName = "spans.json";
    private const string DefaultStateEntryName = "state.json";

    private readonly string? _sourcePath;
    private readonly string _stateEntryName;
    private readonly HashSet<string> _declaredArtifactPaths;

    private ProtoTraceArchive(
        string? sourcePath,
        string formatVersion,
        string runId,
        DateTimeOffset? runStartedAtUtc,
        DateTimeOffset? runCompletedAtUtc,
        IReadOnlyDictionary<string, string?> runAttributes,
        IReadOnlyList<ProtoTraceMoment> runMoments,
        IReadOnlyList<ProtoTraceRecordEvent> runEvidence,
        IReadOnlyList<ProtoTraceArtifact> artifacts,
        IReadOnlyDictionary<string, string> sources,
        string stateEntryName,
        IReadOnlyList<ProtoTraceTest> tests)
    {
        _sourcePath = sourcePath;
        _stateEntryName = stateEntryName;
        FormatVersion = formatVersion;
        RunId = runId;
        RunStartedAtUtc = runStartedAtUtc;
        RunCompletedAtUtc = runCompletedAtUtc;
        RunAttributes = runAttributes;
        RunMoments = runMoments;
        RunEvidence = runEvidence;
        Artifacts = artifacts;
        Sources = sources;
        Tests = tests;
        _declaredArtifactPaths = [.. artifacts.Select(artifact => artifact.ArchivePath)];
        foreach (var test in tests)
        {
            _declaredArtifactPaths.UnionWith(test.Artifacts.Select(artifact => artifact.ArchivePath));
        }
    }

    /// <summary>The file the archive was opened from, when it was opened from one.</summary>
    public string? SourcePath => _sourcePath;

    /// <summary>The span document format version, for example <c>2.0</c>.</summary>
    public string FormatVersion { get; }

    /// <summary>The run's identifier.</summary>
    public string RunId { get; }

    /// <summary>When the run started, when the archive recorded it.</summary>
    public DateTimeOffset? RunStartedAtUtc { get; }

    /// <summary>When the run completed, when the archive recorded it.</summary>
    public DateTimeOffset? RunCompletedAtUtc { get; }

    /// <summary>The run resource's attributes: its identity, timing and the <c>environment.*</c> facts.</summary>
    public IReadOnlyDictionary<string, string?> RunAttributes { get; }

    /// <summary>Moments the run recorded outside any test, for example a run gate's verdict.</summary>
    public IReadOnlyList<ProtoTraceMoment> RunMoments { get; }

    /// <summary>Observations, attachments and findings the run recorded outside any test.</summary>
    public IReadOnlyList<ProtoTraceRecordEvent> RunEvidence { get; }

    /// <summary>The run-level artifacts the archive declares; the content lives behind <see cref="ReadArtifact(ProtoTraceArtifact)"/>.</summary>
    public IReadOnlyList<ProtoTraceArtifact> Artifacts { get; }

    /// <summary>The embedded source files, by the recorded source path; the content lives behind <see cref="ReadSource"/>.</summary>
    public IReadOnlyDictionary<string, string> Sources { get; }

    /// <summary>The tests the run recorded, in archive order.</summary>
    public IReadOnlyList<ProtoTraceTest> Tests { get; }

    /// <summary>Opens a `.prototrace` archive from disk.</summary>
    public static ProtoTraceArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        using var stream = File.OpenRead(fullPath);
        return Read(stream, fullPath);
    }

    /// <summary>Reads a `.prototrace` archive from a stream.</summary>
    /// <remarks>
    /// The stream is read fully before this returns. Artifact and source content and the state document
    /// cannot be read from an archive opened this way; open the file instead.
    /// </remarks>
    public static ProtoTraceArchive Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Read(stream, sourcePath: null);
    }

    /// <summary>
    /// Reads the content of one declared artifact. The archive is opened read-only from the file it was
    /// loaded from; an archive read from a stream has no file to read and says so.
    /// </summary>
    public byte[] ReadArtifact(ProtoTraceArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return ReadArtifact(artifact.ArchivePath);
    }

    /// <summary>Reads the content of the entry at <paramref name="archivePath"/>, which the archive must declare.</summary>
    public byte[] ReadArtifact(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!_declaredArtifactPaths.Contains(archivePath))
        {
            throw new InvalidDataException($"The archive does not declare an artifact at '{archivePath}'.");
        }

        return ReadEntry(archivePath, "artifact");
    }

    /// <summary>Reads an embedded source file's text, or null when the archive does not carry that file.</summary>
    public string? ReadSource(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!Sources.TryGetValue(sourcePath, out var archivePath))
        {
            return null;
        }

        return Encoding.UTF8.GetString(ReadEntry(archivePath, "source"));
    }

    /// <summary>
    /// Reads the run's state document: the tracked items and their changes. Read on request, because a
    /// consumer that only lists runs or failures never needs it; an archive read from a stream has no
    /// file to read and says so.
    /// </summary>
    public ProtoTraceState ReadState()
    {
        var content = ReadEntry(_stateEntryName, "state");
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        var formatVersion = GetString(root, "formatVersion") ?? "unknown";
        if (!formatVersion.StartsWith("1.", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported state format '{formatVersion}'. This reader supports 1.x state documents.");
        }

        var runItems = root.TryGetProperty("run", out var run) && run.ValueKind == JsonValueKind.Object
            ? ReadStateItems(run)
            : [];
        var tests = new List<ProtoTraceStateTest>();
        if (root.TryGetProperty("tests", out var testEntries) && testEntries.ValueKind == JsonValueKind.Array)
        {
            foreach (var test in testEntries.EnumerateArray())
            {
                tests.Add(new ProtoTraceStateTest(
                    GetString(test, "testId") ?? string.Empty,
                    GetString(test, "name") ?? string.Empty,
                    ReadStateItems(test)));
            }
        }

        return new ProtoTraceState(formatVersion, runItems, tests);
    }

    private byte[] ReadEntry(string entryName, string kind)
    {
        if (_sourcePath is null)
        {
            throw new InvalidOperationException(
                $"This archive was read from a stream; open it from its file path to read its {kind} content.");
        }

        using var stream = File.OpenRead(_sourcePath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException($"The archive declares '{entryName}' but the entry is missing.");
        return ReadEntry(entry);
    }

    private static ProtoTraceArchive Read(Stream stream, string? sourcePath)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var manifest = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("The archive has no manifest.json; it is not a ProtoTest trace.");
        using var manifestDocument = JsonDocument.Parse(ReadEntry(manifest));
        var formatVersion = GetString(manifestDocument.RootElement, "formatVersion") ?? "unknown";
        if (!formatVersion.StartsWith("2.", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Unsupported trace format '{formatVersion}'. This reader supports 2.x archives.");
        }

        var spansEntryName = GetString(manifestDocument.RootElement, "spansEntry") ?? DefaultSpansEntryName;
        var spansEntry = archive.GetEntry(spansEntryName)
            ?? throw new InvalidDataException($"The archive declares '{spansEntryName}' but the entry is missing.");
        using var spans = JsonDocument.Parse(ReadEntry(spansEntry));
        if (!spans.RootElement.TryGetProperty("resourceSpans", out var groups))
        {
            throw new InvalidDataException($"The archive's '{spansEntryName}' has no resourceSpans.");
        }

        var stateEntryName = GetString(manifestDocument.RootElement, "stateEntry") ?? DefaultStateEntryName;
        var sources = ReadSources(manifestDocument.RootElement);

        string? runId = null;
        DateTimeOffset? startedAtUtc = null;
        DateTimeOffset? completedAtUtc = null;
        IReadOnlyDictionary<string, string?> runAttributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        IReadOnlyList<ProtoTraceMoment> runMoments = [];
        IReadOnlyList<ProtoTraceRecordEvent> runEvidence = [];
        IReadOnlyList<ProtoTraceArtifact> runArtifacts = [];
        var tests = new List<ProtoTraceTest>();
        foreach (var group in groups.EnumerateArray())
        {
            if (!group.TryGetProperty("resource", out var resource)
                || !resource.TryGetProperty("attributes", out var attributes))
            {
                continue;
            }

            if (attributes.TryGetProperty("testId", out var testId))
            {
                var (moments, evidence) = ReadGroupEvents(group);
                tests.Add(new ProtoTraceTest(
                    testId.GetString() ?? string.Empty,
                    GetString(attributes, "testName") ?? string.Empty,
                    GetString(attributes, "testClass"),
                    GetString(attributes, "testMethod") ?? string.Empty,
                    (GetString(attributes, "testOutcome") ?? "unknown").ToLowerInvariant(),
                    GetDouble(attributes, "testDurationMs") ?? 0,
                    ReadOperations(group))
                {
                    Artifacts = ReadArtifacts(group),
                    Moments = moments,
                    Evidence = evidence
                });
            }
            else if (attributes.TryGetProperty("runId", out var run))
            {
                runId = run.GetString();
                startedAtUtc = GetTimestamp(attributes, "runStartedAtUtc");
                completedAtUtc = GetTimestamp(attributes, "runCompletedAtUtc");
                runAttributes = ReadStringMap(attributes);
                runArtifacts = ReadArtifacts(group);
                (runMoments, runEvidence) = ReadGroupEvents(group);
            }
        }

        return new ProtoTraceArchive(
            sourcePath,
            formatVersion,
            runId ?? "unknown",
            startedAtUtc,
            completedAtUtc,
            runAttributes,
            runMoments,
            runEvidence,
            runArtifacts,
            sources,
            stateEntryName,
            tests);
    }

    private static IReadOnlyDictionary<string, string> ReadSources(JsonElement manifest)
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!manifest.TryGetProperty("sources", out var declared) || declared.ValueKind != JsonValueKind.Object)
        {
            return sources;
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                sources[property.Name] = property.Value.GetString()!;
            }
        }

        return sources;
    }

    private static List<ProtoTraceArtifact> ReadArtifacts(JsonElement group)
    {
        var artifacts = new List<ProtoTraceArtifact>();
        if (!group.TryGetProperty("artifacts", out var declared) || declared.ValueKind != JsonValueKind.Array)
        {
            return artifacts;
        }

        foreach (var artifact in declared.EnumerateArray())
        {
            var archivePath = GetString(artifact, "archivePath");
            if (string.IsNullOrEmpty(archivePath))
            {
                continue;
            }

            artifacts.Add(new ProtoTraceArtifact(
                GetString(artifact, "id") ?? string.Empty,
                GetString(artifact, "name") ?? string.Empty,
                GetString(artifact, "mediaType") ?? string.Empty,
                GetString(artifact, "description"),
                archivePath,
                artifact.TryGetProperty("sizeBytes", out var size) && size.ValueKind == JsonValueKind.Number
                    ? size.GetInt64()
                    : null,
                GetString(artifact, "error")));
        }

        return artifacts;
    }

    private static List<ProtoTraceOperation> ReadOperations(JsonElement group)
    {
        var operations = new List<ProtoTraceOperation>();
        if (!group.TryGetProperty("scopeSpans", out var scopeSpans))
        {
            return operations;
        }

        foreach (var scope in scopeSpans.EnumerateArray())
        {
            if (!scope.TryGetProperty("spans", out var spans))
            {
                continue;
            }

            foreach (var span in spans.EnumerateArray())
            {
                var hasError = span.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object;
                var (moments, evidence) = ReadEvents(span);
                operations.Add(new ProtoTraceOperation(
                    GetString(span, "spanId") ?? string.Empty,
                    GetString(span, "parentSpanId"),
                    GetString(span, "name") ?? string.Empty,
                    GetString(span, "kind") ?? string.Empty,
                    GetString(span, "source") ?? string.Empty,
                    (GetString(span, "phase") ?? string.Empty).ToLowerInvariant(),
                    (GetString(span, "status") ?? "unknown").ToLowerInvariant(),
                    hasError ? GetString(error, "type") : null,
                    hasError ? GetString(error, "message") : null,
                    GetTimestamp(span, "startedAtUtc") ?? default,
                    GetDouble(span, "durationMs"),
                    GetString(span, "entityKind"),
                    GetString(span, "entityId"),
                    span.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object
                        ? ReadStringMap(attributes)
                        : new Dictionary<string, string?>(StringComparer.Ordinal))
                {
                    HasError = hasError,
                    Sections = ReadSections(span),
                    Moments = moments,
                    Evidence = evidence
                });
            }
        }

        return operations;
    }

    private static (IReadOnlyList<ProtoTraceMoment> Moments, IReadOnlyList<ProtoTraceRecordEvent> Evidence) ReadGroupEvents(JsonElement group)
    {
        var moments = new List<ProtoTraceMoment>();
        var evidence = new List<ProtoTraceRecordEvent>();
        if (!group.TryGetProperty("scopeSpans", out var scopeSpans))
        {
            return (moments, evidence);
        }

        foreach (var scope in scopeSpans.EnumerateArray())
        {
            if (!scope.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in events.EnumerateArray())
            {
                ReadEvent(entry, moments, evidence);
            }
        }

        return (moments, evidence);
    }

    private static (IReadOnlyList<ProtoTraceMoment> Moments, IReadOnlyList<ProtoTraceRecordEvent> Evidence) ReadEvents(JsonElement span)
    {
        var moments = new List<ProtoTraceMoment>();
        var evidence = new List<ProtoTraceRecordEvent>();
        if (span.TryGetProperty("events", out var events) && events.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in events.EnumerateArray())
            {
                ReadEvent(entry, moments, evidence);
            }
        }

        return (moments, evidence);
    }

    private static void ReadEvent(
        JsonElement entry,
        List<ProtoTraceMoment> moments,
        List<ProtoTraceRecordEvent> evidence)
    {
        var record = GetString(entry, "record");
        if (record is not null)
        {
            evidence.Add(new ProtoTraceRecordEvent(
                record.ToLowerInvariant(),
                GetString(entry, "name") ?? string.Empty,
                GetTimestamp(entry, "atUtc") ?? default,
                GetString(entry, "kind"),
                GetString(entry, "identifier"),
                GetString(entry, "data"),
                entry.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object
                    ? ReadStringMap(metadata)
                    : new Dictionary<string, string?>(StringComparer.Ordinal),
                GetString(entry, "artifactId"),
                GetString(entry, "status"),
                GetString(entry, "category"),
                GetString(entry, "targetName"),
                ReadTags(entry)));
            return;
        }

        var hasError = entry.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object;
        moments.Add(new ProtoTraceMoment(
            GetString(entry, "name") ?? string.Empty,
            GetTimestamp(entry, "atUtc") ?? default,
            GetString(entry, "kind") ?? string.Empty,
            GetString(entry, "source") ?? string.Empty,
            (GetString(entry, "outcome") ?? "unknown").ToLowerInvariant(),
            hasError ? GetString(error, "type") : null,
            hasError ? GetString(error, "message") : null,
            GetString(entry, "entityKind"),
            GetString(entry, "entityId"),
            entry.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object
                ? ReadStringMap(attributes)
                : new Dictionary<string, string?>(StringComparer.Ordinal),
            ReadSections(entry)));
    }

    private static IReadOnlyList<string> ReadTags(JsonElement entry)
    {
        if (!entry.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. tags.EnumerateArray().Where(tag => tag.ValueKind == JsonValueKind.String).Select(tag => tag.GetString()!)];
    }

    private static IReadOnlyList<ProtoTraceSection> ReadSections(JsonElement entry)
    {
        if (!entry.TryGetProperty("sections", out var sections) || sections.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<ProtoTraceSection>();
        foreach (var section in sections.EnumerateArray())
        {
            var items = new List<ProtoTraceSectionItem>();
            if (section.TryGetProperty("items", out var sectionItems) && sectionItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in sectionItems.EnumerateArray())
                {
                    items.Add(new ProtoTraceSectionItem(
                        GetString(item, "label") ?? string.Empty,
                        GetString(item, "value"),
                        GetString(item, "detail"),
                        (GetString(item, "tone") ?? "neutral").ToLowerInvariant()));
                }
            }

            result.Add(new ProtoTraceSection(
                GetString(section, "label") ?? string.Empty,
                (GetString(section, "kind") ?? string.Empty).ToLowerInvariant(),
                items,
                GetString(section, "content"),
                GetString(section, "language")));
        }

        return result;
    }

    private static List<ProtoTraceStateItem> ReadStateItems(JsonElement container)
    {
        var items = new List<ProtoTraceStateItem>();
        if (!container.TryGetProperty("items", out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            var changes = new List<ProtoTraceStateChange>();
            if (entry.TryGetProperty("changes", out var recorded) && recorded.ValueKind == JsonValueKind.Array)
            {
                foreach (var change in recorded.EnumerateArray())
                {
                    changes.Add(new ProtoTraceStateChange(
                        GetTimestamp(change, "atUtc"),
                        GetString(change, "operationId"),
                        GetString(change, "change") ?? string.Empty,
                        change.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object
                            ? ReadStringMap(state)
                            : new Dictionary<string, string?>(StringComparer.Ordinal),
                        (GetString(change, "source") ?? string.Empty).ToLowerInvariant(),
                        change.TryGetProperty("inferred", out var inferred) && inferred.ValueKind == JsonValueKind.True));
                }
            }

            items.Add(new ProtoTraceStateItem(
                GetString(entry, "kind") ?? string.Empty,
                GetString(entry, "id") ?? string.Empty,
                GetString(entry, "name") ?? string.Empty,
                GetString(entry, "scope"),
                GetTimestamp(entry, "firstSeenUtc"),
                GetTimestamp(entry, "lastSeenUtc"),
                entry.TryGetProperty("state", out var latest) && latest.ValueKind == JsonValueKind.Object
                    ? ReadStringMap(latest)
                    : new Dictionary<string, string?>(StringComparer.Ordinal),
                changes));
        }

        return items;
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static IReadOnlyDictionary<string, string?> ReadStringMap(JsonElement element)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                ? null
                : property.Value.ToString();
        }

        return map;
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static double? GetDouble(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static DateTimeOffset? GetTimestamp(JsonElement element, string property)
    {
        var text = GetString(element, property);
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }
}
