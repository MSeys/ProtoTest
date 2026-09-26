namespace ProtoTest.Core;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ProtoTest.Core.Internal;

internal sealed class ProtoTraceSession : IProtoTraceSource
{
    private readonly ConcurrentDictionary<string, ProtoTestTraceRecorder> _tests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProtoTestTraceRecorder> _testsByTraceId = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<ProtoTraceArtifactSource> _runArtifacts = new();
    private readonly ProtoRunTraceWriter _runWriter = new();
    private readonly ProtoSpanConverter _converter = new();
    private readonly DateTimeOffset _startedAtUtc = DateTimeOffset.UtcNow;
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private DateTimeOffset? _completedAtUtc;
    private readonly ProtoTraceOptions _options;
    private ActivityListener? _activityListener;
    private int _listening;
    private int _runArtifactSequence;
    private int _captureFailureReported;

    public ProtoTraceSession(ProtoTraceOptions? options = null)
    {
        _options = options ?? new ProtoTraceOptions();
    }

    /// <summary>
    /// Starts capturing spans from the watched activity sources. Application instrumentation is ordinary
    /// OpenTelemetry: ProtoTest listens, it does not ask the application to know about ProtoTest.
    /// </summary>
    public void StartListening()
    {
        if (!_options.Enabled || _options.ActivitySources.Count == 0 || Interlocked.Exchange(ref _listening, 1) != 0)
        {
            return;
        }

        var listener = new ActivityListener
        {
            // ProtoTest's own activities are not captured through the listener (their writer already
            // records them semantically); sampling them keeps the W3C trace context alive so application
            // spans can be linked back to the test that caused them.
            ShouldListenTo = source => source.Name == ProtoTestDiagnostics.ActivitySourceName
                || _options.ActivitySources.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped
        };
        ActivitySource.AddActivityListener(listener);
        _activityListener = listener;
    }

    public void StopListening()
    {
        _activityListener?.Dispose();
        _activityListener = null;
        Interlocked.Exchange(ref _listening, 0);
    }

    private void OnActivityStopped(Activity activity)
    {
        if (!_options.Enabled || activity.Source.Name == ProtoTestDiagnostics.ActivitySourceName)
        {
            return;
        }

        try
        {
            // A span that carries a test's trace id belongs to that test even though the callback runs
            // outside its flow - the whole point of propagating context across the app boundary.
            var writer = FindWriter(activity.TraceId);
            if (writer is null)
            {
                var ambient = ProtoHost.CurrentContextOrNull;
                if (ambient is not null)
                {
                    // The ambient context is process-wide: only the session that recorded the test may
                    // capture its spans, or two hosts would both record the same span.
                    if (!Owns(ambient.Trace))
                    {
                        return;
                    }

                    writer = ambient.Trace;
                }
                else if (ProtoHostRegistry.FindTraceSession(activity.TraceId) is not null)
                {
                    // Another host's test owns the span; that host's listener captures it.
                    return;
                }
                else
                {
                    writer = _runWriter;
                }
            }

            var operationId = writer.CaptureActivity(activity);
            _converter.Observe(writer, activity, operationId);
        }
        catch (Exception exception)
        {
            // Capturing telemetry must never break the application, but a broken capture must not be
            // invisible either: the first failure is recorded once as a run event with its exception
            // type and stack, and every later failure coalesces into that one event.
            if (Interlocked.Exchange(ref _captureFailureReported, 1) == 0)
            {
                try
                {
                    _runWriter.WriteEvent(
                        "telemetry.capture_failed",
                        "Telemetry capture failed",
                        "ProtoTest.Core",
                        ProtoTracePhase.Run,
                        ProtoTraceOutcome.Failed,
                        new Dictionary<string, string?>
                        {
                            ["telemetry.source"] = activity.Source.Name,
                            ["telemetry.activity"] = activity.DisplayName
                        },
                        exception);
                }
                catch (Exception)
                {
                    // Recording the failure is best-effort: even that must not break the application.
                }
            }
        }
    }

    /// <summary>Whether a writer is one of this session's test recorders.</summary>
    private bool Owns(IProtoTraceWriter writer)
        => _tests.Values.Any(recorder => ReferenceEquals(recorder, writer));

    /// <inheritdoc />
    public IProtoTraceWriter? FindWriter(ActivityTraceId traceId)
        => _testsByTraceId.TryGetValue(traceId.ToHexString(), out var recorder) ? recorder : null;

    private void RegisterTrace(ActivityTraceId traceId, ProtoTestTraceRecorder recorder)
        => _testsByTraceId.TryAdd(traceId.ToHexString(), recorder);

    public ProtoTestTraceRecorder StartTest(string name, ProtoTestId testId, MethodInfo method)
    {
        var recorder = new ProtoTestTraceRecorder(
            testId.Value, name, method, _options, RegisterTrace, _converter.Forget);
        if (!_tests.TryAdd(testId.Value, recorder))
        {
            throw new InvalidOperationException($"A trace already exists for test ID '{testId.Value}'.");
        }
        return recorder;
    }

    /// <summary>Gets the writer for operations that belong to the run rather than to one test.</summary>
    public IProtoTraceWriter RunWriter => _runWriter;

    /// <summary>Whether automatic trace collection and export are on.</summary>
    internal bool Enabled => _options.Enabled;

    /// <summary>Gets how many completed or active tests still hold observed converter state; used by tests.</summary>
    internal int TrackedWriterCount => _converter.TrackedWriterCount;

    public void CompleteRun() => _completedAtUtc ??= DateTimeOffset.UtcNow;

    public ProtoTraceRun Snapshot()
    {
        var tests = _tests.Values
            .Select(test => test.Snapshot())
            .OrderBy(test => test.StartedAtUtc)
            .ThenBy(test => test.TestId, StringComparer.Ordinal)
            .ToArray();
        var entities = _runWriter.SnapshotEntities();
        var values = _runWriter.SnapshotValues();

        return new ProtoTraceRun(
            // The run snapshot has no separate version of its own: it is serialized as the span
            // document, so its version is the span format's.
            ProtoTraceWire.SpanFormatVersion,
            _runId,
            _startedAtUtc,
            _completedAtUtc,
            tests,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime"] = RuntimeInformation.FrameworkDescription,
                ["os"] = RuntimeInformation.OSDescription,
                ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString()
            },
            _runArtifacts.Select(source => source.Artifact).ToArray(),
            _runWriter.Snapshot(),
            entities,
            values,
            DeriveVisibility(tests, entities, values),
            _runWriter.SnapshotRecord());
    }

    /// <summary>
    /// Visibility is derived from what the trace already contains, so it cannot drift from reality:
    /// capabilities say what was composed, server and client entities say where the application ran, and
    /// the value sources say how deep the integration reached.
    /// </summary>
    private static ProtoTraceVisibility DeriveVisibility(
        IReadOnlyList<ProtoTestTrace> tests,
        IReadOnlyList<ProtoTraceEntity> runEntities,
        IReadOnlyList<ProtoTraceValue> runValues)
    {
        var capabilities = runEntities.Where(entity => entity.Kind == ProtoTraceEntityKinds.Capability).ToArray();
        var testEntities = tests.SelectMany(test => test.Entities ?? []).ToArray();
        var hasServer = capabilities.Any(capability => CapabilityKind(capability) == ProtoCapabilityKinds.Server)
            || testEntities.Any(entity => entity.Kind == ProtoTraceEntityKinds.Server);
        var hasClients = testEntities.Any(entity => entity.Kind == ProtoTraceEntityKinds.Client);
        var hosting = hasServer ? "in-process" : hasClients ? "remote" : "unknown";

        var backends = capabilities
            .Where(capability => CapabilityKind(capability) is
                ProtoCapabilityKinds.Server or ProtoCapabilityKinds.Store or
                ProtoCapabilityKinds.Broker or ProtoCapabilityKinds.Data)
            .Select(capability => capability.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var versions = tests.SelectMany(test => test.Values ?? [])
            .Concat(runValues)
            .SelectMany(value => value.Versions)
            .ToArray();
        // The tokens are the wire's lowercase enum names, so the viewer's change-source vocabulary and
        // the C# derivation cannot drift apart.
        var presentSources = versions.Select(version => version.Source).ToHashSet();
        var sources = new[]
            {
                ProtoTraceValueSource.TestSide,
                ProtoTraceValueSource.Observed,
                ProtoTraceValueSource.ApplicationSide
            }
            .Where(presentSources.Contains)
            .Select(source => ProtoTraceWire.Lower(source))
            .ToArray();
        var applicationInstrumented = presentSources.Contains(ProtoTraceValueSource.ApplicationSide);

        return new ProtoTraceVisibility(hosting, backends, sources, applicationInstrumented);
    }

    private static string? CapabilityKind(ProtoTraceEntity capability)
        => capability.State.TryGetValue("capability.kind", out var kind) ? kind : null;

    internal async Task CaptureRunArtifactsAsync(
        IReadOnlyCollection<ProtoTestAttachment> attachments,
        string sourceName,
        CancellationToken cancellationToken)
    {
        foreach (var attachment in attachments)
        {
            var sequence = Interlocked.Increment(ref _runArtifactSequence);
            var id = $"run-artifact-{sequence}";
            var archivePath = $"resources/run/{ProtoPathSanitizer.FileName(sourceName, "artifact")}/{id}/{ProtoPathSanitizer.FileName(attachment.Name, "artifact")}";
            var artifact = new ProtoTraceArtifact(id, attachment.Name, attachment.MediaType, attachment.Description, archivePath);
            _runArtifacts.Enqueue(await ProtoArtifactCapture.CaptureAsync(
                artifact,
                attachment,
                _options.MaxArtifactBytes,
                _options.EmbedArtifacts,
                cancellationToken));
        }
    }

    internal IReadOnlyList<ProtoTraceArtifactSource> SnapshotArtifactSources()
        => _runArtifacts
            .Concat(_tests.Values.SelectMany(test => test.SnapshotArtifactSources()))
            .ToArray();

}

