namespace ProtoTest.Core;

/// <summary>How a flow treats a failing step.</summary>
public enum ProtoFlowFailureMode
{
    /// <summary>Stop at the first failure and report it.</summary>
    FailFast,

    /// <summary>Run every step and report every failure.</summary>
    Collect
}

/// <summary>Per-step policy: a timeout, a retry budget and which failures are worth retrying.</summary>
public sealed record ProtoStepOptions
{
    /// <summary>No timeout, no retry.</summary>
    public static readonly ProtoStepOptions Default = new();

    /// <summary>Bounds one attempt; a timeout surfaces as a <see cref="TimeoutException"/>.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>How often a failed attempt is retried after the first.</summary>
    public int RetryCount { get; init; }

    /// <summary>Pause between attempts. Zero retries immediately.</summary>
    public TimeSpan RetryDelay { get; init; }

    /// <summary>
    /// Decides whether a failed attempt may be retried. Defaults to every failure except cancellation.
    /// </summary>
    public Func<Exception, bool>? ShouldRetry { get; init; }
}

/// <summary>The failures a flow collected, in step order.</summary>
public sealed record ProtoFlowResult(IReadOnlyList<Exception> Failures)
{
    /// <summary>Whether every step succeeded.</summary>
    public bool Succeeded => Failures.Count == 0;
}

/// <summary>
/// A named, traced sequence of steps: each step is one <c>flow.step</c> operation whose outcome is
/// derived, the flow either stops at the first failure or collects every failure, and a step can
/// carry a timeout and a retry policy. This is the shared sequence primitive for lifecycle phases,
/// teardown chains and journeys.
/// </summary>
public sealed class ProtoFlow
{
    private readonly string _name;
    private readonly string _source;
    private readonly ProtoFlowFailureMode _failureMode;
    private readonly List<FlowStep> _steps = [];

    public ProtoFlow(
        string name,
        string source,
        ProtoFlowFailureMode failureMode = ProtoFlowFailureMode.FailFast)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        _name = name;
        _source = source;
        _failureMode = failureMode;
    }

    /// <summary>Appends a named step.</summary>
    public ProtoFlow Step(string name, Func<CancellationToken, ValueTask> action, ProtoStepOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(action);
        _steps.Add(new FlowStep(name, action, options ?? ProtoStepOptions.Default));
        return this;
    }

    /// <summary>
    /// Runs the steps in order. Cancellation always stops the flow and propagates; step failures are
    /// returned in <see cref="ProtoFlowResult.Failures"/>, so the caller decides whether to throw.
    /// </summary>
    public async ValueTask<ProtoFlowResult> RunAsync(
        IProtoTraceWriter trace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var failures = new List<Exception>();
        for (var index = 0; index < _steps.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await RunStepAsync(trace, _steps[index], index, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                if (_failureMode is ProtoFlowFailureMode.FailFast)
                {
                    break;
                }
            }
        }

        return new ProtoFlowResult(failures);
    }

    private async ValueTask RunStepAsync(
        IProtoTraceWriter trace,
        FlowStep step,
        int index,
        CancellationToken cancellationToken)
    {
        await trace
            .Operation("flow.step", $"{_name} · {step.Name}", _source)
            .With("flow.name", _name)
            .With("step.name", step.Name)
            .With("step.index", index.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .RunAsync(async operation =>
            {
                var attempt = 0;
                while (true)
                {
                    attempt++;
                    try
                    {
                        await ExecuteAttemptAsync(step, cancellationToken);
                        if (attempt > 1)
                        {
                            operation.SetAttribute(
                                "step.attempts",
                                attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }

                        return;
                    }
                    catch (Exception exception) when (
                        attempt <= step.Options.RetryCount
                        && !cancellationToken.IsCancellationRequested
                        && (step.Options.ShouldRetry?.Invoke(exception) ?? true))
                    {
                        operation.SetAttribute(
                            "step.retry",
                            $"{attempt}/{step.Options.RetryCount}");
                        if (step.Options.RetryDelay > TimeSpan.Zero)
                        {
                            await Task.Delay(step.Options.RetryDelay, cancellationToken);
                        }
                    }
                }
            });
    }

    private static async ValueTask ExecuteAttemptAsync(FlowStep step, CancellationToken cancellationToken)
    {
        if (step.Options.Timeout is not { } timeout)
        {
            await step.Action(cancellationToken);
            return;
        }

        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(timeout);
        try
        {
            await step.Action(attempt.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Step '{step.Name}' did not finish within {timeout}.");
        }
    }

    private sealed record FlowStep(string Name, Func<CancellationToken, ValueTask> Action, ProtoStepOptions Options);
}
