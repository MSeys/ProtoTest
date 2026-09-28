namespace ProtoTest.Core.Tests;

using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core.Internal;

/// <summary>
/// The per-test overhead profile: one test cycle broken into the phases the framework owns, measured
/// against a bare host. The phase table feeds the benchmarks page and explains where a suite's
/// per-test milliseconds go; the assertions are generous sanity bounds, not performance targets.
///
/// Each phase is measured on its own entry point, so the numbers do not infer a cost from a
/// difference between two larger loops.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("Benchmark")]
public sealed class PerTestPhaseBenchmarkTests
{
    private const int Warmup = 64;
    private const int Iterations = 512;
    private const int OperationsPerTest = 5;

    private static readonly DateTimeOffset PinnedInstant = new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task PhaseProfile_ShouldStayWithinTheSanityBound()
    {
        var probe = new ProviderProbe();
        // The tracing hosts write their archive at disposal, so the temp traces are declared first and
        // deleted after the hosts are gone.
        using var tracingTrace = new TemporaryTrace("phase-tracing");
        using var tracingNoLocationsTrace = new TemporaryTrace("phase-tracing-nolocations");
        using var quietTrace = new TemporaryTrace("phase-quiet");
        await using var tracing = BuildHost(tracing: true, probe, tracingTrace.Path);
        await using var tracingNoLocations = BuildHost(tracing: true, probe, tracingNoLocationsTrace.Path, captureSourceLocations: false);
        await using var quiet = BuildHost(tracing: false, probe, quietTrace.Path);
        await tracing.StartAsync();
        await tracingNoLocations.StartAsync();
        await quiet.StartAsync();

        var measurements = new List<Measured>
        {
            await ScopePhaseAsync(tracing, probe)
        };
        var (contextCreate, contextDispose) = await ContextPhasesAsync(probe);
        measurements.Add(contextCreate);
        measurements.Add(contextDispose);
        measurements.Add(await MeasureAsync("clock.register", Warmup, Iterations, _ =>
        {
            var registry = new ProtoClockRegistry();
            registry.Add("00001", new ProtoClock(PinnedInstant));
            registry.Remove("00001");
            return Task.CompletedTask;
        }));
        measurements.Add(await MeasureAsync("recorder.disabled", Warmup, Iterations, _ =>
            RecorderPhaseAsync(enabled: false)));
        measurements.Add(await MeasureAsync("recorder.enabled", Warmup, Iterations, _ =>
            RecorderPhaseAsync(enabled: true)));
        measurements.Add(await MeasureAsync("recorder.enabled-nolocations", Warmup, Iterations, _ =>
            RecorderPhaseAsync(enabled: true, captureSourceLocations: false)));
        measurements.Add(await MeasureAsync("prepare", Warmup, Iterations, _ =>
        {
            ProtoTestAdapter.Prepare(TestMethods.Placeholder, tracing, "phase");
            return Task.CompletedTask;
        }));
        measurements.Add(await MeasureAsync("prepare.attributed", Warmup, Iterations, _ =>
        {
            ProtoTestAdapter.Prepare(AttributedPhaseMethod, tracing, "phase");
            return Task.CompletedTask;
        }));
        measurements.AddRange(await CyclePhaseAsync(quiet, "cycle.quiet", 30000, exercise: null));
        measurements.AddRange(await CyclePhaseAsync(tracing, "cycle.tracing", 40000, exercise: null));
        measurements.AddRange(await CyclePhaseAsync(tracingNoLocations, "cycle.tracing-nolocations", 70000, exercise: null));
        measurements.AddRange(await CyclePhaseAsync(tracing, "cycle.operations", 50000, context =>
        {
            for (var index = 0; index < OperationsPerTest; index++)
            {
                context.Trace
                    .Operation("benchmark.operation", "Benchmark operation", "ProtoTest.Core.Tests")
                    .Begin()
                    .Succeed();
            }

            return Task.CompletedTask;
        }));
        measurements.AddRange(await CyclePhaseAsync(tracing, "cycle.attachment", 60000, context =>
        {
            context.AddAttachment("benchmark", "payload");
            return Task.CompletedTask;
        }));

        TestContext.Progress.WriteLine(
            "[phase] host=bare iterations=" + Iterations.ToString(CultureInfo.InvariantCulture));
        foreach (var measurement in measurements)
        {
            TestContext.Progress.WriteLine(measurement.Line());
        }

        Assert.Multiple(() =>
        {
            foreach (var measurement in measurements)
            {
                Assert.That(
                    measurement.MedianMs, Is.LessThan(25),
                    $"the {measurement.Name} phase stays far below 25 ms");
            }
        });
    }

    private static ProtoHost BuildHost(
        bool tracing,
        ProviderProbe probe,
        string outputPath,
        bool captureSourceLocations = true)
    {
        var builder = new ProtoHostBuilder()
            .ConfigureTracing(options =>
            {
                options.Enabled = tracing;
                options.OutputPath = outputPath;
                options.EmbedSources = false;
                options.EmbedArtifacts = false;
                options.CaptureSourceLocations = captureSourceLocations;
            })
            .ConfigureServices(services => services.AddSingleton(serviceProvider =>
            {
                probe.Provider = serviceProvider;
                return probe;
            }));
        return builder.Build();
    }

    /// <summary>DI scope creation and disposal against the host's own provider.</summary>
    private static async Task<Measured> ScopePhaseAsync(ProtoHost host, ProviderProbe probe)
    {
        var context = await host.StartTestAsync("phase", "00001", TestMethods.Placeholder);
        _ = context.Service<ProviderProbe>();
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        Assert.That(probe.Provider, Is.Not.Null, "the host provider is captured by the registered probe");

        return await MeasureAsync("scope.create+dispose", Warmup, Iterations, _ =>
        {
            using var scope = probe.Provider!.CreateScope();
            return Task.CompletedTask;
        });
    }

    /// <summary>Context creation against a scope the harness owns, then context disposal.</summary>
    private static async Task<(Measured Create, Measured Dispose)> ContextPhasesAsync(ProviderProbe probe)
    {
        for (var index = 0; index < Warmup; index++)
        {
            using var scope = probe.Provider!.CreateScope();
            await new ProtoExecutionContext("phase", scope, ProtoTestId.Parse("00001"), TestMethods.Placeholder)
                .DisposeAsync();
        }

        var contexts = new List<ProtoExecutionContext>(Iterations);
        var created = new List<double>(Iterations);
        var createAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        for (var index = 0; index < Iterations; index++)
        {
            var started = Stopwatch.StartNew();
            contexts.Add(new ProtoExecutionContext(
                "phase", probe.Provider!.CreateScope(), ProtoTestId.Parse("00001"), TestMethods.Placeholder));
            started.Stop();
            created.Add(started.Elapsed.TotalMilliseconds);
        }

        var createAllocated = GC.GetTotalAllocatedBytes(precise: true) - createAllocatedBefore;
        var disposed = new List<double>(Iterations);
        var disposeAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        foreach (var context in contexts)
        {
            var started = Stopwatch.StartNew();
            await context.DisposeAsync();
            started.Stop();
            disposed.Add(started.Elapsed.TotalMilliseconds);
        }

        var disposeAllocated = GC.GetTotalAllocatedBytes(precise: true) - disposeAllocatedBefore;
        return (
            Summarize("context.create", created, createAllocated / (double)Iterations / 1024),
            Summarize("context.dispose", disposed, disposeAllocated / (double)Iterations / 1024));
    }

    /// <summary>The trace recorder's own per-test work: construct, record operations, complete.</summary>
    private static Task RecorderPhaseAsync(bool enabled, bool captureSourceLocations = true)
    {
        var options = new ProtoTraceOptions
        {
            Enabled = enabled,
            EmbedSources = false,
            EmbedArtifacts = false,
            CaptureSourceLocations = captureSourceLocations
        };
        var recorder = new ProtoTestTraceRecorder("00001", "phase", TestMethods.Placeholder, options);
        for (var index = 0; index < OperationsPerTest; index++)
        {
            recorder
                .StartOperation("benchmark.operation", "Benchmark operation", "ProtoTest.Core.Tests")
                .Succeed();
        }

        recorder.CompleteTest(ProtoTestResult.Passed);
        return Task.CompletedTask;
    }

    /// <summary>A full lifecycle cycle, timed per phase; the exercise runs between start and complete.</summary>
    private static async Task<IReadOnlyList<Measured>> CyclePhaseAsync(
        ProtoHost host,
        string phase,
        int firstTestNumber,
        Func<ProtoExecutionContext, Task>? exercise)
    {
        var sequence = 0;
        async Task<CycleSample> CycleAsync()
        {
            var totalAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var total = Stopwatch.StartNew();
            var startAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var started = Stopwatch.StartNew();
            var context = await host.StartTestAsync(
                phase,
                (firstTestNumber + sequence++).ToString("D5", CultureInfo.InvariantCulture),
                TestMethods.Placeholder);
            started.Stop();
            var startAllocated = GC.GetTotalAllocatedBytes(precise: true) - startAllocatedBefore;
            if (exercise is not null)
            {
                await exercise(context);
            }

            var completeAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var completed = Stopwatch.StartNew();
            await host.CompleteTestAsync(ProtoTestResult.Passed);
            completed.Stop();
            var completeAllocated = GC.GetTotalAllocatedBytes(precise: true) - completeAllocatedBefore;
            total.Stop();
            var totalAllocated = GC.GetTotalAllocatedBytes(precise: true) - totalAllocatedBefore;
            return new CycleSample(
                started.Elapsed.TotalMilliseconds,
                completed.Elapsed.TotalMilliseconds,
                total.Elapsed.TotalMilliseconds,
                startAllocated / 1024.0,
                completeAllocated / 1024.0,
                totalAllocated / 1024.0);
        }

        for (var index = 0; index < Warmup; index++)
        {
            await CycleAsync();
        }

        var starts = new List<double>(Iterations);
        var completes = new List<double>(Iterations);
        var totals = new List<double>(Iterations);
        var startAllocations = new List<double>(Iterations);
        var completeAllocations = new List<double>(Iterations);
        var totalAllocations = new List<double>(Iterations);
        for (var index = 0; index < Iterations; index++)
        {
            var sample = await CycleAsync();
            starts.Add(sample.StartMs);
            completes.Add(sample.CompleteMs);
            totals.Add(sample.TotalMs);
            startAllocations.Add(sample.StartKb);
            completeAllocations.Add(sample.CompleteKb);
            totalAllocations.Add(sample.TotalKb);
        }

        startAllocations.Sort();
        completeAllocations.Sort();
        totalAllocations.Sort();
        return
        [
            Summarize(phase + ".start", starts, startAllocations[startAllocations.Count / 2]),
            Summarize(phase + ".complete", completes, completeAllocations[completeAllocations.Count / 2]),
            Summarize(phase + ".total", totals, totalAllocations[totalAllocations.Count / 2])
        ];
    }

    private sealed record CycleSample(
        double StartMs,
        double CompleteMs,
        double TotalMs,
        double StartKb,
        double CompleteKb,
        double TotalKb);

    private static async Task<Measured> MeasureAsync(
        string name,
        int warmup,
        int iterations,
        Func<int, Task> action)
    {
        for (var index = 0; index < warmup; index++)
        {
            await action(index);
        }

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var samples = new List<double>(iterations);
        for (var index = 0; index < iterations; index++)
        {
            var started = Stopwatch.StartNew();
            await action(index);
            started.Stop();
            samples.Add(started.Elapsed.TotalMilliseconds);
        }

        var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
        return Summarize(name, samples, allocated / (double)iterations / 1024);
    }

    private static Measured Summarize(string name, List<double> samples, double allocatedKb = 0)
    {
        samples.Sort();
        return new Measured(
            name,
            samples[samples.Count / 2],
            samples[(int)Math.Ceiling(0.95 * samples.Count) - 1],
            allocatedKb);
    }

    private static readonly MethodInfo AttributedPhaseMethod =
        typeof(PerTestPhaseBenchmarkTests).GetMethod(
            nameof(AttributedPhasePlaceholder), BindingFlags.NonPublic | BindingFlags.Static)!;

    [RequiresCapability(ProtoCapabilityKinds.Server)]
    private static void AttributedPhasePlaceholder()
    {
    }

    private sealed record Measured(string Name, double MedianMs, double P95Ms, double AllocatedKbPerOperation)
    {
        public string Line() =>
            "[phase] phase=" + Name +
            " median=" + MedianMs.ToString("F4", CultureInfo.InvariantCulture) + " ms" +
            " p95=" + P95Ms.ToString("F4", CultureInfo.InvariantCulture) + " ms" +
            " allocated=" + AllocatedKbPerOperation.ToString("F1", CultureInfo.InvariantCulture) + " KB";
    }

    /// <summary>Captures the host's root provider through a singleton the harness registers.</summary>
    private sealed class ProviderProbe
    {
        public IServiceProvider? Provider { get; set; }
    }
}
