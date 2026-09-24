namespace ProtoTest.Traces;

using System.Globalization;
using System.IO.Compression;
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
    /// <summary>The source file the operation started in, when the trace captured locations.</summary>
    public string? SourceFile => Attribute("code.file.path");

    /// <summary>The source line the operation started on, when the trace captured locations.</summary>
    public int? SourceLine
        => int.TryParse(Attribute("code.line.number"), CultureInfo.InvariantCulture, out var line) ? line : null;

    /// <summary>The source function the operation started in, when the trace captured locations.</summary>
    public string? SourceFunction => Attribute("code.function.name");

    /// <summary>Whether this operation recorded a failure.</summary>
    public bool Failed => string.Equals(Status, "failed", StringComparison.Ordinal);

    private string? Attribute(string name) => Attributes.TryGetValue(name, out var value) ? value : null;
}

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
    /// <summary>Whether the test recorded a full success.</summary>
    public bool Succeeded => string.Equals(Outcome, "succeeded", StringComparison.Ordinal);

    /// <summary>
    /// The operation that best explains a non-succeeded test: the execution operation when it failed,
    /// otherwise the first failed operation, otherwise the execution operation.
    /// </summary>
    public ProtoTraceOperation? Failure
        => Operations.FirstOrDefault(operation => operation.Kind == "test.execution" && operation.Failed)
            ?? Operations.FirstOrDefault(operation => operation.Failed)
            ?? Operations.FirstOrDefault(operation => operation.Kind == "test.execution");
}

/// <summary>
/// A read-only view of one `.prototrace` archive. This reader depends on nothing but the wire
/// contract, so tools and agents can consume traces without taking a dependency on ProtoTest.Core.
/// </summary>
public sealed class ProtoTraceArchive
{
    private const string ManifestEntryName = "manifest.json";
    private const string DefaultSpansEntryName = "spans.json";

    private ProtoTraceArchive(
        string formatVersion,
        string runId,
        DateTimeOffset? runStartedAtUtc,
        DateTimeOffset? runCompletedAtUtc,
        IReadOnlyList<ProtoTraceTest> tests)
    {
        FormatVersion = formatVersion;
        RunId = runId;
        RunStartedAtUtc = runStartedAtUtc;
        RunCompletedAtUtc = runCompletedAtUtc;
        Tests = tests;
    }

    /// <summary>The span document format version, for example <c>2.0</c>.</summary>
    public string FormatVersion { get; }

    /// <summary>The run's identifier.</summary>
    public string RunId { get; }

    /// <summary>When the run started, when the archive recorded it.</summary>
    public DateTimeOffset? RunStartedAtUtc { get; }

    /// <summary>When the run completed, when the archive recorded it.</summary>
    public DateTimeOffset? RunCompletedAtUtc { get; }

    /// <summary>The tests the run recorded, in archive order.</summary>
    public IReadOnlyList<ProtoTraceTest> Tests { get; }

    /// <summary>Opens a `.prototrace` archive from disk.</summary>
    public static ProtoTraceArchive Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    /// <summary>Reads a `.prototrace` archive from a stream.</summary>
    public static ProtoTraceArchive Read(Stream stream)
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

        string? runId = null;
        DateTimeOffset? startedAtUtc = null;
        DateTimeOffset? completedAtUtc = null;
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
                tests.Add(new ProtoTraceTest(
                    testId.GetString() ?? string.Empty,
                    GetString(attributes, "testName") ?? string.Empty,
                    GetString(attributes, "testClass"),
                    GetString(attributes, "testMethod") ?? string.Empty,
                    GetString(attributes, "testOutcome") ?? "unknown",
                    GetDouble(attributes, "testDurationMs") ?? 0,
                    ReadOperations(group)));
            }
            else if (attributes.TryGetProperty("runId", out var run))
            {
                runId = run.GetString();
                startedAtUtc = GetTimestamp(attributes, "runStartedAtUtc");
                completedAtUtc = GetTimestamp(attributes, "runCompletedAtUtc");
            }
        }

        return new ProtoTraceArchive(formatVersion, runId ?? "unknown", startedAtUtc, completedAtUtc, tests);
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
                operations.Add(new ProtoTraceOperation(
                    GetString(span, "spanId") ?? string.Empty,
                    GetString(span, "parentSpanId"),
                    GetString(span, "name") ?? string.Empty,
                    GetString(span, "kind") ?? string.Empty,
                    GetString(span, "source") ?? string.Empty,
                    GetString(span, "phase") ?? string.Empty,
                    GetString(span, "status") ?? "unknown",
                    hasError ? GetString(error, "type") : null,
                    hasError ? GetString(error, "message") : null,
                    GetTimestamp(span, "startedAtUtc") ?? default,
                    GetDouble(span, "durationMs"),
                    GetString(span, "entityKind"),
                    GetString(span, "entityId"),
                    span.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object
                        ? ReadStringMap(attributes)
                        : new Dictionary<string, string?>(StringComparer.Ordinal)));
            }
        }

        return operations;
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
