namespace ProtoTest.Core.Tests;

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// The cross-cutting consistency rules: collectors
/// are isolated, options validate once at resolve, a completed recorder never observes again, and two
/// hosts do not capture the same test span.
/// </summary>
[TestFixture]
public sealed class ProtoConsistencyTests
{
    [Test]
    public async Task ThrowingCollector_ShouldNotFailTheTestAndShouldBeTraced()
    {
        // Arrange
        var recording = new RecordingCollector();
        var builder = new ProtoHostBuilder();
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IProtoCollector>(new ThrowingCollector());
            services.AddSingleton<IProtoCollector>(recording);
        });
        await using var host = builder.Build();
        await host.StartAsync();
        var context = await host.StartTestAsync("collector isolation", "00060", TestMethods.Placeholder);

        // Act
        context.RecordObservation("Target", "probe.kind", "id-1");

        // Assert
        await host.CompleteTestAsync(ProtoTestResult.Passed);
        var test = host.Trace.Snapshot().Tests.Single();
        Assert.Multiple(() =>
        {
            // The runner result is the passed result passed to CompleteTestAsync; the trace marks the
            // partial because a collector failed while the test itself did not.
            Assert.That(test.Outcome, Is.EqualTo(ProtoTraceOutcome.Partial));
            Assert.That(test.Error, Is.Null, "the collector bug is not the test's own error");
            Assert.That(recording.Observations, Has.Count.EqualTo(1), "the collectors behind the failing one still run");
            Assert.That(
                test.Entries.Any(entry => entry.Kind == "collector.failed"),
                Is.True,
                "the collector failure is traced");
            Assert.That(test.Record!.Observations, Is.Not.Empty, "the observation itself is still evidence");
        });
        await host.StopAsync();
    }

    [Test]
    public void ResolveOptions_ShouldValidateTheBoundResult()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProtoOptionsRegistration.Resolve(provider, () => new ValidatedOptions { Value = 0 }));
    }

    [Test]
    public void CompletedRecorder_ShouldNotObserveLaterSpans()
    {
        // Arrange
        var converter = new ProtoSpanConverter();
        // The session wires CompleteTest to Forget; mirror that so the race under test is reachable.
        var recorder = new ProtoTestTraceRecorder(
            "00061",
            "span",
            TestMethods.Placeholder,
            onCompleted: converter.Forget);
        const string Source = "ProtoTest.Core.Tests.CompletedRecorder";
        using var listener = new ActivityListener
        {
            ShouldListenTo = activitySource => activitySource.Name == Source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource(Source);
        using (var span = source.StartActivity("invoice.pay"))
        {
            span!.SetTag("invoice.number", "INV-2");
            converter.Observe(recorder, span, null);
        }

        Assert.That(converter.TrackedWriterCount, Is.EqualTo(1));
        recorder.CompleteTest(ProtoTestResult.Passed);

        // Act: late telemetry must not resurrect the state CompleteTest released.
        using (var late = source.StartActivity("invoice.pay"))
        {
            late!.SetTag("invoice.number", "INV-3");
            converter.Observe(recorder, late, null);
        }

        // Assert
        Assert.That(converter.TrackedWriterCount, Is.EqualTo(0));
    }

    [Test]
    [NonParallelizable]
    public async Task TwoHosts_ShouldCaptureATestSpanOnlyOnce()
    {
        // Arrange
        const string Source = "ProtoTest.Core.Tests.MultiHost";
        var first = CreateHost(Source);
        var second = CreateHost(Source);
        try
        {
            await first.StartAsync();
            await second.StartAsync();
            using var source = new ActivitySource(Source);
            var context = await first.StartTestAsync("multi host", "00062", TestMethods.Placeholder);

            // Act: the app span is a child of the test's operation, so it carries the test's trace id.
            using (var operation = context.Trace.Operation("probe", "Probe", "Test").Begin())
            {
                using (source.StartActivity("app.work"))
                {
                }

                operation.Succeed();
            }

            await first.CompleteTestAsync(ProtoTestResult.Passed);

            // Assert
            var owningTest = first.Trace.Snapshot().Tests.Single();
            var otherRun = second.Trace.Snapshot();
            Assert.Multiple(() =>
            {
                Assert.That(
                    owningTest.Entries.Count(entry => entry.Name == "app.work"),
                    Is.EqualTo(1),
                    "the owning host captures the span once");
                Assert.That(otherRun.Entries, Is.Empty, "the other host must not record the span");
                Assert.That(otherRun.Tests, Is.Empty);
            });

            await first.StopAsync();
            await second.StopAsync();
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }
    }

    private static ProtoHost CreateHost(string source)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options =>
        {
            options.OutputPath = Path.Combine(
                Path.GetTempPath(),
                $"prototest-consistency-{Guid.NewGuid():N}.prototrace");
            options.ActivitySources.Add(source);
        });
        return builder.Build();
    }

    private sealed class ThrowingCollector : IProtoCollector
    {
        public bool CanCollect(ProtoObservation observation) => true;

        public void Collect(ProtoObservation observation)
            => throw new InvalidOperationException("The collector failed.");
    }

    private sealed class RecordingCollector : IProtoCollector
    {
        public List<ProtoObservation> Observations { get; } = [];

        public bool CanCollect(ProtoObservation observation) => true;

        public void Collect(ProtoObservation observation) => Observations.Add(observation);
    }

    private sealed class ValidatedOptions : IProtoConfigurableOptions
    {
        public string ConfigurationSectionName => "ProtoTest:Tests:Validated";

        public int Value { get; set; } = 1;

        public void Validate()
        {
            if (Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Value), Value, "Value must be positive.");
            }
        }
    }
}
