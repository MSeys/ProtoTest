namespace ProtoTest.Core.Tests;

using System.IO.Compression;
using System.Text.Json;
using ProtoTest.TestSupport;

/// <summary>
/// The CI run metadata a suite records on the run wire and in
/// the report, and the unchanged run when none is configured.
/// </summary>
[TestFixture]
public sealed class RunMetadataTests
{
    [Test]
    public async Task RunMetadata_ShouldRecordExplicitValuesOnTheRunWireAndInTheReport()
    {
        // An explicit option is the direct way to say which run produced a trace.
        using var trace = new TemporaryTrace("run-metadata-explicit");
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = trace.Path;
            options.RunMetadata["ci.provider"] = "github";
            options.RunMetadata["ci.run_id"] = "42";
        });
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        var environment = host.Trace.Snapshot().Environment;
        using var archive = ZipFile.OpenRead(trace.Path);
        using var spans = JsonDocument.Parse(ReadEntry(archive, "spans.json"));
        var runAttributes = RunAttributes(spans);
        var items = sink.Items
            .Where(item => item.Kind == ProtoReportItemKinds.RunMetadata)
            .OrderBy(item => item.Identifier, StringComparer.Ordinal)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(environment["ci.provider"], Is.EqualTo("github"));
            Assert.That(environment["ci.run_id"], Is.EqualTo("42"));
            Assert.That(
                runAttributes.GetProperty("environment.ci.provider").GetString(),
                Is.EqualTo("github"),
                "the metadata rides the run resource's environment attributes");
            Assert.That(runAttributes.GetProperty("environment.ci.run_id").GetString(), Is.EqualTo("42"));
            Assert.That(items, Has.Length.EqualTo(2));
            Assert.That(items[0].Identifier, Is.EqualTo("ci.provider"));
            Assert.That(items[0].Message, Is.EqualTo("github"));
            Assert.That(items[0].TargetName, Is.EqualTo("Run"));
            Assert.That(items[1].Identifier, Is.EqualTo("ci.run_id"));
            Assert.That(items[1].Message, Is.EqualTo("42"));
        }
    }

    [Test]
    public async Task RunMetadata_ShouldLiftNamedEnvironmentVariables()
    {
        // The CI facts usually already exist as environment variables, so the suite names them
        // instead of copying their values. The variable name is unique to this test, and no other test
        // reads it, so setting it process-wide stays parallel-safe.
        const string name = "PROTOTEST_A515_RUN_ID";
        Environment.SetEnvironmentVariable(name, "run-123");
        try
        {
            using var trace = new TemporaryTrace("run-metadata-environment");
            var sink = new CapturingSink();
            var builder = new ProtoHostBuilder();
            builder.AddSink(sink);
            builder.ConfigureTracing(options =>
            {
                options.OutputPath = trace.Path;
                options.RunMetadataEnvironmentVariables.Add(name);
            });
            await using var host = builder.Build();

            await host.StartAsync();
            await host.StopAsync();

            using var archive = ZipFile.OpenRead(trace.Path);
            using var spans = JsonDocument.Parse(ReadEntry(archive, "spans.json"));
            var item = sink.Items.Single(candidate => candidate.Kind == ProtoReportItemKinds.RunMetadata);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(host.Trace.Snapshot().Environment[name], Is.EqualTo("run-123"));
                Assert.That(
                    RunAttributes(spans).GetProperty($"environment.{name}").GetString(),
                    Is.EqualTo("run-123"));
                Assert.That(item.Identifier, Is.EqualTo(name));
                Assert.That(item.Message, Is.EqualTo("run-123"));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Test]
    public async Task RunMetadata_ShouldIgnoreANamedVariableThatIsNotSet()
    {
        // Optional in every environment - a named variable a local machine does not set leaves
        // both the wire and the report exactly as they were.
        const string name = "PROTOTEST_A515_NOT_SET";
        Environment.SetEnvironmentVariable(name, null);
        using var trace = new TemporaryTrace("run-metadata-unset");
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = trace.Path;
            options.RunMetadataEnvironmentVariables.Add(name);
        });
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        using var archive = ZipFile.OpenRead(trace.Path);
        using var spans = JsonDocument.Parse(ReadEntry(archive, "spans.json"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.Trace.Snapshot().Environment.ContainsKey(name), Is.False);
            Assert.That(RunAttributes(spans).TryGetProperty($"environment.{name}", out _), Is.False);
            Assert.That(sink.Items.Where(item => item.Kind == ProtoReportItemKinds.RunMetadata), Is.Empty);
        }
    }

    [Test]
    public async Task RunMetadata_ExplicitValueShouldWinOverTheLiftedVariable()
    {
        // The same name may arrive twice - once from the environment, once from the suite - and
        // the explicit value is the more specific fact.
        const string name = "PROTOTEST_A515_RUN_ID_PRECEDENCE";
        Environment.SetEnvironmentVariable(name, "from-environment");
        try
        {
            using var trace = new TemporaryTrace("run-metadata-precedence");
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options =>
            {
                options.OutputPath = trace.Path;
                options.RunMetadataEnvironmentVariables.Add(name);
                options.RunMetadata[name] = "explicit";
            });
            await using var host = builder.Build();

            await host.StartAsync();
            await host.StopAsync();

            Assert.That(host.Trace.Snapshot().Environment[name], Is.EqualTo("explicit"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Test]
    public async Task RunMetadata_WhenAbsent_ShouldLeaveTheWireAndReportUnchanged()
    {
        // Characterization, run green before the feature landed: with no run metadata configured, the run
        // records exactly the built-in environment facts and the report carries no item for it, so a
        // local run is the same trace and report as before the option existed.
        using var trace = new TemporaryTrace("run-metadata-absent");
        var sink = new CapturingSink();
        var builder = new ProtoHostBuilder();
        builder.AddSink(sink);
        builder.ConfigureTracing(options => options.OutputPath = trace.Path);
        await using var host = builder.Build();

        await host.StartAsync();
        await host.StopAsync();

        var environment = host.Trace.Snapshot().Environment;
        using var archive = ZipFile.OpenRead(trace.Path);
        using var spans = JsonDocument.Parse(ReadEntry(archive, "spans.json"));
        var wireEnvironment = RunAttributes(spans).EnumerateObject()
            .Where(property => property.Name.StartsWith("environment.", StringComparison.Ordinal))
            .Select(property => property.Name["environment.".Length..])
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                environment.Keys,
                Is.EquivalentTo(new[] { "runtime", "os", "processArchitecture", "osArchitecture" }),
                "the built-in environment facts are the run's whole environment");
            Assert.That(wireEnvironment, Is.EquivalentTo(environment.Keys), "the wire carries the same facts");
            Assert.That(sink.Items, Is.Empty, "no run metadata means no report item");
        }
    }

    [Test]
    public void RunMetadata_ShouldRejectABlankKey()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.RunMetadata[" "] = "value");

        var exception = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.That(exception!.Message, Does.Contain("RunMetadata"));
    }

    [Test]
    public void RunMetadata_ShouldRejectABlankEnvironmentVariableName()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.RunMetadataEnvironmentVariables.Add(" "));

        var exception = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.That(exception!.Message, Does.Contain("RunMetadataEnvironmentVariables"));
    }

    [Test]
    public void RunMetadata_ShouldRejectANullValue()
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.RunMetadata["ci.run_id"] = null!);

        var exception = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.That(exception!.Message, Does.Contain("ci.run_id"));
    }

    [Test]
    public void RunMetadata_ShouldRejectABuiltInEnvironmentKey()
    {
        // The built-in facts describe the platform the run executed on; a metadata key that names one
        // of them would hide it, so it is a configuration error instead of a silent replacement.
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.RunMetadata["os"] = "hidden");

        var exception = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.That(exception!.Message, Does.Contain("built-in environment fact"));
    }

    private static byte[] ReadEntry(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static JsonElement RunAttributes(JsonDocument spans)
        => spans.RootElement.GetProperty("resourceSpans").EnumerateArray()
            .Single(group => group.GetProperty("resource").GetProperty("attributes").TryGetProperty("runId", out _))
            .GetProperty("resource").GetProperty("attributes");
}
