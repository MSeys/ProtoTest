namespace ProtoTest.Core;

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class ProtoTraceArchiveWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static async Task WriteAsync(
        string path,
        ProtoTraceRun run,
        IReadOnlyList<ProtoTraceArtifactSource>? artifacts = null,
        CancellationToken cancellationToken = default)
    {
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

        await WriteJsonAsync(
            archive,
            "manifest.json",
            new ProtoTraceManifest(run.FormatVersion, "run.json"),
            cancellationToken);
        await WriteJsonAsync(
            archive,
            "run.json",
            run.CompletedAtUtc is null
                ? run with { CompletedAtUtc = DateTimeOffset.UtcNow }
                : run,
            cancellationToken);
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

    private sealed record ProtoTraceManifest(string FormatVersion, string RunEntry);
}
