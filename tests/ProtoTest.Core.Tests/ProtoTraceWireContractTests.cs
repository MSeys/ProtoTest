namespace ProtoTest.Core.Tests;

using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;
using NUnit.Framework;
using ProtoTest.Core;

/// <summary>
/// Pins the wire against design/prototrace-wire.contract.json: the writer's constants must match it, and
/// the committed golden archive must still match it, so a format change cannot land half-mirrored.
/// </summary>
[TestFixture]
public sealed class ProtoTraceWireContractTests
{
    [Test]
    public void WriterConstants_ShouldMatchTheSharedContract()
    {
        var contract = Contract();

        Assert.Multiple(() =>
        {
            Assert.That(
                contract.GetProperty("spanFormatVersion").GetString(),
                Is.EqualTo(ProtoTraceWire.SpanFormatVersion));
            Assert.That(
                contract.GetProperty("stateFormatVersion").GetString(),
                Is.EqualTo(ProtoTraceWire.StateFormatVersion));
            Assert.That(
                contract.GetProperty("archiveFormatVersion").GetString(),
                Is.EqualTo(ProtoTraceArchiveWriter.ArchiveFormatVersion));
            Assert.That(
                contract.GetProperty("valueSourceTokens").EnumerateArray().Select(token => token.GetString()),
                Is.EqualTo(Enum.GetValues<ProtoTraceValueSource>().Select(source => ProtoTraceWire.Lower(source))),
                "the source tokens the writer emits must be the ones the viewer normalizes");
        });
    }

    [Test]
    public void GoldenArchive_ShouldMatchTheSharedContract()
    {
        var contract = Contract();
        var path = Path.Combine(RepositoryRoot(), "viewer", "public", "demos", "prototest-demo.prototrace");
        Assert.That(
            File.Exists(path),
            Is.True,
            $"The golden archive '{path}' is missing; regenerate it with eng/generate-recipe-traces.ps1.");

        using var archive = ZipFile.OpenRead(path);
        var manifest = ReadJson(archive, "manifest.json");
        var spans = ReadJson(archive, "spans.json");
        var state = ReadJson(archive, "state.json");

        Assert.Multiple(() =>
        {
            Assert.That(
                manifest.GetProperty("formatVersion").GetString(),
                Is.EqualTo(contract.GetProperty("archiveFormatVersion").GetString()));
            Assert.That(manifest.GetProperty("spansEntry").GetString(), Is.EqualTo("spans.json"));
            Assert.That(manifest.GetProperty("stateEntry").GetString(), Is.EqualTo("state.json"));
            Assert.That(
                spans.GetProperty("formatVersion").GetString(),
                Is.EqualTo(contract.GetProperty("spanFormatVersion").GetString()));
            Assert.That(
                state.GetProperty("formatVersion").GetString(),
                Is.EqualTo(contract.GetProperty("stateFormatVersion").GetString()));
            Assert.That(spans.GetProperty("resourceSpans").GetArrayLength(), Is.GreaterThan(0));
        });    }

    private static JsonElement Contract()
    {
        var path = Path.Combine(RepositoryRoot(), "design", "prototrace-wire.contract.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static JsonElement ReadJson(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)?.Open()
            ?? throw new InvalidOperationException($"The golden archive has no {name}.");
        using var reader = new StreamReader(stream);
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        return document.RootElement.Clone();
    }

    private static string RepositoryRoot([CallerFilePath] string? sourcePath = null)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourcePath)!);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProtoTest.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root was not found from the test source path.");
    }
}
