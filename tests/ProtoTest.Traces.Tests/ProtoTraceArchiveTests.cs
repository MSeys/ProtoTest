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
                // The viewer's selector: the deepest failing operation, not the phase span above it.
                Assert.That(failed.Failure!.Kind, Is.EqualTo("reader.check"));
                Assert.That(failed.Failure.ErrorMessage, Does.Contain("The check failed."));
                Assert.That(failed.Failure.SourceFile, Is.Not.Null);
                Assert.That(
                    failed.Operations.Any(operation => operation.Kind == "reader.check" && operation.Failed),
                    Is.True);
                Assert.That(archive.RunAttributes["environment.runtime"], Does.Contain(".NET"));
            }

            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Archive_ShouldReadSectionsEvidenceAndState()
    {
        await TraceFixtures.WithTraceAsync(path =>
        {
            // Act
            var archive = ProtoTraceArchive.Open(path);
            var failed = archive.Tests.Single(test => test.Name == "reader fail");
            var check = failed.Failure!;
            var state = archive.ReadState();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(check.Sections, Has.Count.EqualTo(1));
                Assert.That(check.Sections[0].Kind, Is.EqualTo("checks"));
                Assert.That(check.Sections[0].Items[0].Tone, Is.EqualTo("error"));
                Assert.That(
                    failed.Evidence.Select(evidence => evidence.Record),
                    Is.EquivalentTo(new[] { "observation", "attachment", "finding" }));
                Assert.That(
                    failed.Evidence.Single(evidence => evidence.Record == "finding").Status,
                    Is.EqualTo("Warning"));
                Assert.That(failed.Artifacts, Has.Count.EqualTo(1));
                Assert.That(state.FormatVersion, Is.EqualTo("1.1"));
                var item = state.ItemsFor(failed.TestId).Single(candidate => candidate.Id == "reader:value-1");
                Assert.That(item.State["reader.state"], Is.EqualTo("checked"));
                Assert.That(item.Changes, Has.Count.EqualTo(1));
                Assert.That(item.Changes[0].OperationId, Is.EqualTo(check.SpanId));
                Assert.That(item.Changes[0].Change, Is.EqualTo("changed"));
            }

            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Archive_ShouldReadTheArtifactsItDeclaresAndRefuseAnythingElse()
    {
        await TraceFixtures.WithTraceAsync(path =>
        {
            // Act
            var archive = ProtoTraceArchive.Open(path);
            var failed = archive.Tests.Single(test => test.Name == "reader fail");
            var artifact = failed.Artifacts.Single();

            // Assert
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.Name, Does.EndWith("reader-failure.json"));
                Assert.That(artifact.MediaType, Is.EqualTo("application/json"));
                Assert.That(artifact.ArchivePath, Does.StartWith("resources/"));
                Assert.That(
                    System.Text.Encoding.UTF8.GetString(archive.ReadArtifact(artifact)),
                    Is.EqualTo("""{"reason":"check"}"""));
                Assert.Throws<InvalidDataException>(() => archive.ReadArtifact("resources/not-declared/file"));
            }

            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task Archive_ShouldRejectArtifactContentWhenItWasReadFromAStream()
    {
        await TraceFixtures.WithTraceAsync(path =>
        {
            // Act
            using var stream = File.OpenRead(path);
            var archive = ProtoTraceArchive.Read(stream);
            var artifact = archive.Tests.Single(test => test.Name == "reader fail").Artifacts.Single();

            // Assert
            var exception = Assert.Throws<InvalidOperationException>(() => archive.ReadArtifact(artifact));
            Assert.That(exception!.Message, Does.Contain("file path"));
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
