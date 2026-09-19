namespace ProtoTest.Core;

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class ProtoTraceArchiveWriter
{
    /// <summary>
    /// The archive layout: a manifest naming the two v2 documents, plus the artifact files they declare.
    /// 2.0 dropped the run.json compatibility view; a reader that finds no spans entry has an older trace.
    /// </summary>
    private const string ArchiveFormatVersion = "2.0";

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static async Task WriteAsync(
        string path,
        ProtoTraceRun run,
        IReadOnlyList<ProtoTraceArtifactSource>? artifacts = null,
        bool embedSources = false,
        CancellationToken cancellationToken = default)
    {
        // Every document describes a finished run, so an open run is closed once here rather than per document.
        run = run.CompletedAtUtc is null ? run with { CompletedAtUtc = DateTimeOffset.UtcNow } : run;
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var destination = File.Create(fullPath);
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var source in artifacts ?? [])
        {
            if (source.Artifact.Error is not null) continue;
            var entry = archive.CreateEntry(source.Artifact.ArchivePath, CompressionLevel.NoCompression);
            await using var stream = entry.Open();
            await stream.WriteAsync(source.Content, cancellationToken);
        }

        var sources = embedSources ? await WriteSourcesAsync(archive, run, cancellationToken) : null;

        await WriteJsonAsync(
            archive,
            "manifest.json",
            new ProtoTraceManifest(ArchiveFormatVersion, "spans.json", "state.json", sources),
            cancellationToken);
        await WriteJsonAsync(archive, "spans.json", ProtoTraceWire.Spans(run), cancellationToken);
        await WriteJsonAsync(archive, "state.json", ProtoTraceWire.State(run), cancellationToken);
    }

    /// <summary>
    /// Copies each source file an operation's location points at, once, and returns where each landed. A file
    /// that is gone or too large is left out; the location still names it.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, string>?> WriteSourcesAsync(
        ZipArchive archive,
        ProtoTraceRun run,
        CancellationToken cancellationToken)
    {
        const long MaxSourceBytes = 512 * 1024;
        var paths = run.Tests.SelectMany(test => test.Entries)
            .Concat(run.Entries ?? [])
            .Select(entry => entry.Attributes.GetValueOrDefault(ProtoSourceLocator.FilePathAttribute))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            try
            {
                var file = new FileInfo(ProtoSourceLocator.Resolve(path));
                if (!file.Exists || file.Length > MaxSourceBytes) continue;
                var archivePath = $"sources/{sources.Count + 1}/{file.Name}";
                var entry = archive.CreateEntry(archivePath, CompressionLevel.NoCompression);
                await using var stream = entry.Open();
                await using var content = file.OpenRead();
                await content.CopyToAsync(stream, cancellationToken);
                sources[path] = archivePath;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return sources.Count == 0 ? null : sources;
    }

    private static async Task WriteJsonAsync<T>(
        ZipArchive archive,
        string name,
        T value,
        CancellationToken cancellationToken)
    {
        // Stored entries let the static viewer read the archive without shipping a ZIP dependency.
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record ProtoTraceManifest(
        string FormatVersion,
        string SpansEntry,
        string StateEntry,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? Sources);
}
