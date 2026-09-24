namespace ProtoTest.Traces.Tests;

using System.IO.Compression;

/// <summary>
/// The trace reader against a real archive: the run's identity, each test's outcome, and the failure
/// digest the CLI prints.
/// </summary>
[TestFixture]
public sealed class ProtoTraceArchiveTests
{
    [Test]
    public async Task Archive_ShouldExposeTheRunTestsAndTheFailure()
    {
        await TraceFixtures.WithTraceAsync(path =>
        {
            // Act
            var archive = ProtoTraceArchive.Open(path);

            // Assert
            var failed = archive.Tests.Single(test => test.Name == "reader fail");
            var passed = archive.Tests.Single(test => test.Name == "reader pass");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(archive.FormatVersion, Is.EqualTo("2.0"));
                Assert.That(archive.RunId, Is.Not.Empty);
                Assert.That(archive.Tests, Has.Count.EqualTo(2));
                Assert.That(passed.Succeeded, Is.True);
                Assert.That(failed.Outcome, Is.EqualTo("failed"));
                Assert.That(failed.Failure!.Kind, Is.EqualTo("test.execution"));
                Assert.That(failed.Failure.ErrorMessage, Does.Contain("The check failed."));
                Assert.That(
                    failed.Operations.Any(operation => operation.Kind == "reader.check" && operation.Failed),
                    Is.True);
            }

            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Summary_ShouldPrintTheFailureDigestAndSkipPassingTests()
    {
        await TraceFixtures.WithTraceAsync(path =>
        {
            // Act
            var archive = ProtoTraceArchive.Open(path);
            using var writer = new StringWriter();
            ProtoTraceSummaryText.Write(archive, writer);
            var summary = writer.ToString();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(summary, Does.Contain("ProtoTest trace 2.0"));
                Assert.That(summary, Does.Contain("1 succeeded"));
                Assert.That(summary, Does.Contain("1 failed"));
                Assert.That(summary, Does.Contain("FAILED reader fail"));
                Assert.That(summary, Does.Contain("The check failed."));
                Assert.That(summary, Does.Not.Contain("reader pass"));
            }

            return Task.CompletedTask;
        });
    }

    [Test]
    public void Archive_ShouldRejectAFileThatIsNotATrace()
    {
        var path = Path.Combine(Path.GetTempPath(), $"prototest-not-a-trace-{Guid.NewGuid():N}.prototrace");
        try
        {
            using (var stream = File.Create(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                using var entry = new StreamWriter(zip.CreateEntry("hello.txt").Open());
                entry.Write("not a trace");
            }

            Assert.Throws<InvalidDataException>(() => ProtoTraceArchive.Open(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
