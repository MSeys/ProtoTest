namespace ProtoTest.Core.Tests;

using System.Diagnostics;

/// <summary>
/// Stage 6 (Audit 3, finding G1): the scale measurement harness. It runs synthetic tests through the
/// real lifecycle and records trace size, run time, stop/export time and allocation growth, with
/// generous sanity bounds so a regression that makes the trace explode fails the suite. The measured
/// numbers feed the benchmarks page.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Scale")]
public sealed class TraceScaleTests
{
    [TestCase(100)]
    [TestCase(1000)]
    public async Task SyntheticTests_ShouldExportWithinTheScaleBudget(int count)
    {
        var output = Path.Combine(Path.GetTempPath(), $"prototest-scale-{count}-{Guid.NewGuid():N}.prototrace");
        try
        {
            var builder = new ProtoHostBuilder();
            builder.ConfigureTracing(options =>
            {
                options.OutputPath = output;
                options.EmbedSources = false;
                options.EmbedArtifacts = false;
            });
            await using var host = builder.Build();
            await host.StartAsync();

            var process = Process.GetCurrentProcess();
            var peakBefore = process.PeakWorkingSet64;
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var runWatch = Stopwatch.StartNew();

            for (var index = 0; index < count; index++)
            {
                var context = await host.StartTestAsync(
                    $"scale {index}",
                    index.ToString("D5"),
                    TestMethods.Placeholder);
                using (var operation = context.Trace
                    .Operation("scale.operation", $"Scale {index}", "ProtoTest.Core.Tests")
                    .With("scale.index", index.ToString())
                    .Begin())
                {
                    context.Trace.WriteEvent(
                        "scale.event",
                        "Worked",
                        "ProtoTest.Core.Tests",
                        attributes: new Dictionary<string, string?> { ["scale.index"] = index.ToString() });
                    context.Trace.SetEntityState(
                        "scale",
                        $"item:{index % 50}",
                        $"Item {index % 50}",
                        new Dictionary<string, string?> { ["scale.value"] = index.ToString() });
                    context.RecordObservation("Scale", "scale.observation", $"item:{index % 50}");
                    operation.Succeed();
                }

                await host.CompleteTestAsync(ProtoTestResult.Passed);
            }

            runWatch.Stop();
            var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;

            var stopWatch = Stopwatch.StartNew();
            await host.StopAsync();
            stopWatch.Stop();

            process.Refresh();
            var peakGrowth = process.PeakWorkingSet64 - peakBefore;
            var size = new FileInfo(output).Length;

            TestContext.Progress.WriteLine(
                $"[scale] tests={count} trace={size / 1024.0 / 1024.0:F2} MB " +
                $"run={runWatch.ElapsedMilliseconds} ms stop+export={stopWatch.ElapsedMilliseconds} ms " +
                $"allocated={allocated / 1024.0 / 1024.0:F0} MB peak-working-set-growth={peakGrowth / 1024.0 / 1024.0:F0} MB");

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(output), Is.True, "the trace is written");
                Assert.That(
                    size,
                    Is.LessThan(count * 64L * 1024),
                    "the trace stays proportional to the test count");
            });
        }
        finally
        {
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }
}
