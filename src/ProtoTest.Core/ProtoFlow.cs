namespace ProtoTest.Core;

using System.Globalization;

/// <summary>How a flow treats a failing step.</summary>
public enum ProtoFlowFailureMode
{
    /// <summary>Stop at the first failure and report it.</summary>
    FailFast,

    /// <summary>Run every step and report every failure.</summary>
    Collect
}

/// <summary>The failures a flow collected, in step order.</summary>
public sealed record ProtoFlowResult(IReadOnlyList<Exception> Failures)
{
    /// <summary>Whether every step succeeded.</summary>
    public bool Succeeded => Failures.Count == 0;
}

/// <summary>
/// One step of a flow: the operation the step records and the entity that operation belongs to.
/// Declaring the operation is what lets a teardown chain keep its semantic entry - a resource release
/// stays <c>resource.release</c>, an attribute teardown stays <c>attribute.after</c> - instead of
/// collapsing into a generic step. A step declared by name alone records as <c>flow.step</c>.
/// </summary>
public sealed record ProtoStepDescriptor(
    string Kind,
    string Name,
    string Source,
    ProtoTracePhase Phase = ProtoTracePhase.Execution,
    IReadOnlyDictionary<string, string?>? Attributes = null,
    string? EntityKind = null,
    string? EntityId = null);

/// <summary>
/// A named, traced sequence of steps: each step is one operation whose outcome is derived, and the
/// flow either stops at the first failure or collects every failure. This is the shared sequence
/// primitive for lifecycle phases and teardown chains, so a release chain reads as a list of steps
/// instead of a loop, an exception list and a tracing helper.
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

    /// <summary>Appends a step traced as the generic <c>flow.step</c> operation.</summary>
    public ProtoFlow Step(string name, Func<CancellationToken, ValueTask> action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Step(
            new ProtoStepDescriptor(
                "flow.step",
                $"{_name} · {name}",
                _source,
                Attributes: new Dictionary<string, string?> { ["step.name"] = name }),
            action);
    }

    /// <summary>Appends a step traced as the operation the descriptor declares.</summary>
    public ProtoFlow Step(ProtoStepDescriptor step, Func<CancellationToken, ValueTask> action)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(action);
        _steps.Add(new FlowStep(step, action));
        return this;
    }

    /// <summary>
    /// Runs the steps in order. Cancelling the flow's token always stops the flow and propagates; in
    /// collect mode a step that cancels for any other reason is recorded like any failure and the
    /// remaining steps still run, so teardown never skips cleanup. Step failures are returned in
    /// <see cref="ProtoFlowResult.Failures"/>, so the caller decides whether to throw.
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
            catch (OperationCanceledException exception)
                when (_failureMode is ProtoFlowFailureMode.Collect && !cancellationToken.IsCancellationRequested)
            {
                // A cancelled teardown step is cleanup that failed, not a stop signal: record it and
                // run the rest. A cancelled flow token still propagates below.
                failures.Add(exception);
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
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["flow.name"] = _name,
            ["step.index"] = index.ToString(CultureInfo.InvariantCulture)
        };
        if (step.Descriptor.Attributes is not null)
        {
            foreach (var (key, value) in step.Descriptor.Attributes)
            {
                attributes[key] = value;
            }
        }

        var scope = trace
            .Operation(step.Descriptor.Kind, step.Descriptor.Name, step.Descriptor.Source)
            .During(step.Descriptor.Phase)
            .With(attributes);
        if (step.Descriptor.EntityKind is not null && step.Descriptor.EntityId is not null)
        {
            scope = scope.For(step.Descriptor.EntityKind, step.Descriptor.EntityId);
        }

        await scope.RunAsync(_ => step.Action(cancellationToken));
    }

    private sealed record FlowStep(ProtoStepDescriptor Descriptor, Func<CancellationToken, ValueTask> Action);
}
