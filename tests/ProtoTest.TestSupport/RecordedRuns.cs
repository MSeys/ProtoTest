namespace ProtoTest.TestSupport;

using ProtoTest.Core;

/// <summary>One operation a recorded test performs; a failure ends the test there.</summary>
/// <remarks>A step with an empty kind records nothing and waits for <paramref name="Pause"/>: an untraced gap.</remarks>
public sealed record RecordedStep(string Kind, string Name, string? Subject = null, Exception? Failure = null, TimeSpan Pause = default);

/// <summary>One test of a recorded run: its name and the operations it performs in order.</summary>
public sealed record RecordedTest(string Name, params RecordedStep[] Steps)
{
    /// <summary>Records the test as skipped, the way a runner reports a test it did not run.</summary>
    public bool Skipped { get; init; }
}

/// <summary>
/// Writes a real run through the host builder from a short description, so tests that read two runs
/// (comparison, proof, review) exercise the archive round trip instead of a hand-built model.
/// </summary>
public static class RecordedRuns
{
    /// <summary>The trace source the recorded operations carry.</summary>
    public const string Source = "ProtoTest.TestSupport.RecordedRuns";

    /// <summary>A step that succeeds.</summary>
    public static RecordedStep Call(string name, string subject) => new("http.request", name, subject);

    /// <summary>A step that fails with the given error.</summary>
    public static RecordedStep FailedCall(string name, string subject, Exception failure)
        => new("http.request", name, subject, failure);

    /// <summary>A wait that records no operation, the way a sleep in a test body does.</summary>
    public static RecordedStep Sleep(TimeSpan duration) => new(string.Empty, "sleep", Pause: duration);

    /// <summary>A browser navigation; the URL is part of the name, as the web backends record it.</summary>
    public static RecordedStep Navigate(string url) => new("web.navigate", $"WEB · Navigate · {url}");

    /// <summary>A check that succeeds.</summary>
    public static RecordedStep Check(string name) => new("assert.json.shape", name);

    /// <summary>A check that fails with the given error.</summary>
    public static RecordedStep FailedCheck(string name, Exception failure) => new("assert.json.shape", name, null, failure);

    /// <summary>Records the tests as one run in the trace at <paramref name="path"/>.</summary>
    public static Task WriteAsync(string path, params RecordedTest[] tests) => WriteAsync(path, null, tests);

    /// <summary>Records the tests as one run, with <paramref name="configure"/> adding sinks or services.</summary>
    public static async Task WriteAsync(string path, Action<ProtoHostBuilder>? configure, params RecordedTest[] tests)
    {
        var builder = new ProtoHostBuilder();
        builder.ConfigureTracing(options => options.OutputPath = path);
        configure?.Invoke(builder);
        await using var host = builder.Build();
        await host.StartAsync();
        var number = 0;
        foreach (var test in tests)
        {
            var context = await host.StartTestAsync(test.Name, $"{++number:00000}", TestMethods.Placeholder);
            Exception? failure = null;
            foreach (var step in test.Steps)
            {
                if (step.Kind.Length == 0)
                {
                    await Task.Delay(step.Pause);
                    continue;
                }

                var operation = context.Trace.Operation(step.Kind, step.Name, Source);
                if (step.Subject is { Length: > 0 } subject)
                {
                    operation = operation.With("request.identifier", subject);
                }

                using var scope = operation.Begin();
                if (step.Failure is not null)
                {
                    scope.Fail(step.Failure);
                    failure = step.Failure;
                    break;
                }

                scope.Succeed();
            }

            await host.CompleteTestAsync(test.Skipped
                ? ProtoTestResult.Skipped
                : failure is null ? ProtoTestResult.Passed : ProtoTestResult.Failed(failure));
        }

        await host.StopAsync();
    }
}
