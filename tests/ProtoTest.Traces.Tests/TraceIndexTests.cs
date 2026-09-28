namespace ProtoTest.Traces.Tests;

using ProtoTest.Cli;
using ProtoTest.Diagnosis;

/// <summary>The static index command: the page over a folder of runs, the digest beside each archive, and the exit codes.</summary>
[TestFixture]
public sealed class TraceIndexTests
{
    [Test]
    public async Task Index_ShouldWriteThePageWithOutcomesFailuresAndLinks()
    {
        var folder = TraceFolders.Create();
        try
        {
            var mixed = Path.Combine(folder, "older.prototrace");
            var passing = Path.Combine(folder, "newer.prototrace");
            await TraceFixtures.WriteMixedAsync(mixed);
            await TraceFixtures.WritePassingAsync(passing);
            File.WriteAllText(Path.Combine(folder, "broken.prototrace"), "not a trace");

            using var output = new StringWriter();
            using var error = new StringWriter();
            var exit = CliHost.Run(["index", folder], output, error);

            var page = File.ReadAllText(Path.Combine(folder, "index.html"));
            var mixedId = ProtoTraceArchive.Open(mixed).RunId;
            var passingId = ProtoTraceArchive.Open(passing).RunId;
            var newerPosition = page.IndexOf(passingId, StringComparison.Ordinal);
            var olderPosition = page.IndexOf(mixedId, StringComparison.Ordinal);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(0));
                Assert.That(error.ToString(), Is.Empty);
                Assert.That(output.ToString(), Does.Contain("Indexed 2 runs"));
                Assert.That(newerPosition, Is.GreaterThanOrEqualTo(0));
                Assert.That(olderPosition, Is.GreaterThan(newerPosition));
                Assert.That(page, Does.Contain("older.prototrace"));
                Assert.That(page, Does.Contain("older.digest.json"));
                Assert.That(page, Does.Contain("1 succeeded").And.Contain("1 failed").And.Contain("1 skipped"));
                Assert.That(page, Does.Contain("00082").And.Contain("index fail"));
                Assert.That(page, Does.Contain("00081").And.Contain("index skip"));
                Assert.That(page, Does.Contain("all succeeded"));
                Assert.That(page, Does.Contain("Not indexed").And.Contain("broken.prototrace"));
                Assert.That(page, Does.Not.Contain("\u2013").And.Not.Contain("\u2014"), "the page carries plain hyphens only");
            }

            // The digest beside the archive is the one diagnosis digest, byte for byte.
            var digest = File.ReadAllText(Path.Combine(folder, "older.digest.json"));
            Assert.That(digest, Is.EqualTo(ProtoDiagnosisJson.ToJson(ProtoDiagnosis.Read(mixed))));
        }
        finally
        {
            TraceFolders.Delete(folder);
        }
    }

    [Test]
    public async Task Index_ShouldWriteTheSamePageForTheSameFolder()
    {
        var folder = TraceFolders.Create();
        try
        {
            await TraceFixtures.WriteMixedAsync(Path.Combine(folder, "older.prototrace"));
            using (var output = new StringWriter())
            using (var error = new StringWriter())
            {
                Assert.That(CliHost.Run(["index", folder], output, error), Is.EqualTo(0));
            }

            var page = File.ReadAllBytes(Path.Combine(folder, "index.html"));
            var digest = File.ReadAllBytes(Path.Combine(folder, "older.digest.json"));

            using (var output = new StringWriter())
            using (var error = new StringWriter())
            {
                Assert.That(CliHost.Run(["index", folder], output, error), Is.EqualTo(0));
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.ReadAllBytes(Path.Combine(folder, "index.html")), Is.EqualTo(page));
                Assert.That(File.ReadAllBytes(Path.Combine(folder, "older.digest.json")), Is.EqualTo(digest));
            }
        }
        finally
        {
            TraceFolders.Delete(folder);
        }
    }

    [Test]
    public void Index_ShouldReturnOneForAMissingFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prototest-missing-{Guid.NewGuid():N}");
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["index", folder], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("Trace folder not found").And.Contain(Path.GetFileName(folder)));
            Assert.That(output.ToString(), Is.Empty);
        }
    }

    [Test]
    public void Index_ShouldReturnOneWhenNoArchiveIsReadable()
    {
        var folder = TraceFolders.Create();
        try
        {
            File.WriteAllText(Path.Combine(folder, "broken.prototrace"), "not a trace");
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exit = CliHost.Run(["index", folder], output, error);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(exit, Is.EqualTo(1));
                Assert.That(error.ToString(), Does.Contain("No readable").And.Contain("broken.prototrace"));
                Assert.That(File.Exists(Path.Combine(folder, "index.html")), Is.False);
            }
        }
        finally
        {
            TraceFolders.Delete(folder);
        }
    }

    [Test]
    public void IndexWithoutAFolder_ShouldReturnOneAndPrintUsage()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliHost.Run(["index"], output, error);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exit, Is.EqualTo(1));
            Assert.That(error.ToString(), Does.Contain("usage: prototest summary").And.Contain("prototest index <folder>"));
        }
    }
}
