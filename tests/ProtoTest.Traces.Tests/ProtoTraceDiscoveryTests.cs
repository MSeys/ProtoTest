namespace ProtoTest.Traces.Tests;

/// <summary>The folder scan the index command and the MCP tools share: readable runs, their order and named skips.</summary>
[TestFixture]
public sealed class ProtoTraceDiscoveryTests
{
    [Test]
    public async Task Discover_ShouldReturnReadableRunsNewestFirstAndSkipUnreadableArchives()
    {
        var folder = TraceFolders.Create();
        try
        {
            var older = Path.Combine(folder, "older.prototrace");
            var newer = Path.Combine(folder, "newer.prototrace");
            var broken = Path.Combine(folder, "broken.prototrace");
            await TraceFixtures.WriteMixedAsync(older);
            await TraceFixtures.WritePassingAsync(newer);
            File.WriteAllText(broken, "not a trace");

            var discovered = ProtoTraceDiscovery.Discover(folder);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(discovered.Root, Is.EqualTo(Path.GetFullPath(folder)));
                Assert.That(discovered.Runs.Count, Is.EqualTo(2));
                Assert.That(discovered.Runs[0].TraceFile, Is.EqualTo(Path.GetFullPath(newer)));
                Assert.That(discovered.Runs[1].TraceFile, Is.EqualTo(Path.GetFullPath(older)));
                Assert.That(discovered.Skipped.Count, Is.EqualTo(1));
                Assert.That(discovered.Skipped[0].TraceFile, Is.EqualTo(Path.GetFullPath(broken)));
                Assert.That(discovered.Skipped[0].Reason, Is.Not.Empty);
            }
        }
        finally
        {
            TraceFolders.Delete(folder);
        }
    }

    [Test]
    public async Task Discover_ShouldWalkTheTreeWhenTestResultsHoldsOnlyAnUnreadableArchive()
    {
        var folder = TraceFolders.Create();
        try
        {
            var testResults = Directory.CreateDirectory(Path.Combine(folder, "TestResults")).FullName;
            var broken = Path.Combine(testResults, "broken.prototrace");
            File.WriteAllText(broken, "not a trace");
            var nested = Directory.CreateDirectory(Path.Combine(folder, "nested")).FullName;
            var readable = Path.Combine(nested, "run-passed.prototrace");
            await TraceFixtures.WritePassingAsync(readable);

            var discovered = ProtoTraceDiscovery.Discover(folder);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(discovered.Runs.Count, Is.EqualTo(1), "an unreadable TestResults archive must not hide a readable run elsewhere");
                Assert.That(discovered.Runs[0].TraceFile, Is.EqualTo(Path.GetFullPath(readable)));
                Assert.That(discovered.Skipped.Count, Is.EqualTo(1));
                Assert.That(discovered.Skipped[0].TraceFile, Is.EqualTo(Path.GetFullPath(broken)));
            }
        }
        finally
        {
            TraceFolders.Delete(folder);
        }
    }

    [Test]
    public async Task Discover_ShouldReadTestResultsUnderBuildOutputAndNothingElseThere()
    {
        var folder = TraceFolders.Create();
        try
        {
            var output = Directory.CreateDirectory(Path.Combine(folder, "Shop.Tests", "bin", "Release", "net10.0")).FullName;
            var results = Directory.CreateDirectory(Path.Combine(output, "TestResults", "TestResults")).FullName;
            var run = Path.Combine(output, "TestResults", "run.prototrace");
            var nestedRun = Path.Combine(results, "nested.prototrace");
            var copiedFixture = Path.Combine(Directory.CreateDirectory(Path.Combine(output, "Fixtures")).FullName, "fixture.prototrace");
            await TraceFixtures.WritePassingAsync(run);
            await TraceFixtures.WritePassingAsync(nestedRun);
            await TraceFixtures.WritePassingAsync(copiedFixture);

            var discovered = ProtoTraceDiscovery.Discover(folder);

            Assert.That(
                discovered.Runs.Select(found => found.TraceFile),
                Is.EquivalentTo(new[] { Path.GetFullPath(run), Path.GetFullPath(nestedRun) }),
                "a default trace is visible, read once, and a fixture copied to the output is not a run");
        }
        finally
        {
            TraceFolders.Delete(folder);
        }
    }

    [Test]
    public void Discover_ShouldNameAMissingFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"prototest-missing-{Guid.NewGuid():N}");

        var exception = Assert.Throws<DirectoryNotFoundException>(() => ProtoTraceDiscovery.Discover(folder));

        Assert.That(
            exception!.Message,
            Does.Contain("Trace folder not found").And.Contain(Path.GetFileName(folder)));
    }
}
