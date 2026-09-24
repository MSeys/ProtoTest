namespace ProtoTest.Core.Tests;

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Stage 1 (Audit 3, findings A4 and G1): the evidence boundary honors the tracing switch and an
/// artifact size cap. Disabled tracing keeps in-memory recording for gates and tests but installs no
/// activity listener and captures no sink artifacts; an over-limit attachment is recorded as an error
/// artifact without its content.
/// </summary>
[TestFixture]
public sealed class ProtoEvidenceBoundaryTests
{
    [Test]
    public async Task DisabledTracing_ShouldNotListenToConfiguredActivitySources()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.Enabled = false;
            options.ActivitySources.Add("ProtoTest.Tests.DisabledTracing");
        });
        await using var host = builder.Build();
        await host.StartAsync();
        using var source = new ActivitySource("ProtoTest.Tests.DisabledTracing");

        // Act
        using (source.StartActivity("work"))
        {
        }

        await host.StopAsync();

        // Assert
        Assert.That(host.Trace.Snapshot().Entries, Is.Empty);
    }

    [Test]
    public async Task DisabledTracing_ShouldNotCaptureSinkArtifacts()
    {
        // Arrange
        var builder = new ProtoHostBuilder();
        builder.AddSink(new ArtifactSink());
        builder.ConfigureTracing(options => options.Enabled = false);
        await using var host = builder.Build();

        // Act
        await host.StartAsync();
        await host.StopAsync();

        // Assert: the report was exported, but its artifact was not read into a run that writes nothing.
        var session = (ProtoTraceSession)host.Trace;
        Assert.That(session.SnapshotArtifactSources(), Is.Empty);
    }

    [Test]
    public async Task EnabledTracing_ShouldCaptureSinkArtifacts()
    {
        // The counterpart pin: with export on, the sink artifact is part of the archive.
        var output = Path.Combine(Path.GetTempPath(), $"prototest-sink-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.AddSink(new ArtifactSink());
            builder.ConfigureTracing(options => options.OutputPath = output);
            await using var host = builder.Build();

            await host.StartAsync();
            await host.StopAsync();

            var session = (ProtoTraceSession)host.Trace;
            Assert.That(session.SnapshotArtifactSources(), Has.Count.EqualTo(1));
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task ArtifactsOverTheLimit_ShouldBeRecordedWithoutContent()
    {
        // Arrange
        var output = Path.Combine(Path.GetTempPath(), $"prototest-limit-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options =>
            {
                options.OutputPath = output;
                options.MaxArtifactBytes = 8;
            });
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("limit", "00050", TestMethods.Placeholder);
            context.AddAttachment(ProtoTestAttachment.FromText("big.txt", new string('x', 64)));

            // Act
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.StopAsync();

            // Assert
            var artifact = host.Trace.Snapshot().Tests.Single().Artifacts.Single();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.SizeBytes, Is.EqualTo(64));
                Assert.That(artifact.Error, Does.Contain("exceeds the configured artifact limit"));
            }

            using var archive = System.IO.Compression.ZipFile.OpenRead(output);
            Assert.That(
                archive.Entries.Any(entry => entry.FullName == artifact.ArchivePath),
                Is.False,
                "an over-limit artifact contributes no bytes to the archive");
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Test]
    public async Task DisabledArtifactEmbedding_ShouldDeclareWithoutContent()
    {
        // Stage 6: the artifacts-off lever. The attachment is declared - name, media type, error - but
        // nothing is read or written.
        var output = Path.Combine(Path.GetTempPath(), $"prototest-no-embed-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options =>
            {
                options.OutputPath = output;
                options.EmbedArtifacts = false;
            });
            await using var host = builder.Build();
            await host.StartAsync();
            var context = await host.StartTestAsync("no embed", "00051", TestMethods.Placeholder);
            context.AddAttachment(ProtoTestAttachment.FromText("note.txt", "content"));

            await host.CompleteTestAsync(ProtoTestResult.Passed);
            await host.StopAsync();

            var artifact = host.Trace.Snapshot().Tests.Single().Artifacts.Single();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.Error, Does.Contain("embedding is disabled"));
                Assert.That(artifact.SizeBytes, Is.Null);
            }

            using var archive = System.IO.Compression.ZipFile.OpenRead(output);
            Assert.That(
                archive.Entries.Any(entry => entry.FullName == artifact.ArchivePath),
                Is.False,
                "an unembedded artifact contributes no bytes to the archive");
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    private sealed class ArtifactSink : IProtoSink, IProtoSinkArtifactSource
    {
        public Task ExportAsync(IEnumerable<ProtoReportItem> items, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public IReadOnlyCollection<ProtoTestAttachment> GetArtifacts()
            => [ProtoTestAttachment.FromText("report.txt", "report")];
    }
}
