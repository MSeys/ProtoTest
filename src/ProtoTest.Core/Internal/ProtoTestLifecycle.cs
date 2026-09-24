namespace ProtoTest.Core.Internal;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

internal sealed class ProtoTestLifecycle
{
    private static readonly AsyncLocal<ContextState?> Current = new();
    private readonly ProtoHost _host;
    private readonly IServiceProvider _rootServiceProvider;
    private readonly IReadOnlyList<IProtoTestHook> _hooks;
    private readonly IProtoTestIdGenerator _testIdGenerator;
    private readonly ProtoTraceSession _trace;

    public ProtoTestLifecycle(
        ProtoHost host,
        IServiceProvider rootServiceProvider,
        IEnumerable<IProtoTestHook> hooks,
        IProtoTestIdGenerator testIdGenerator,
        ProtoTraceSession trace)
    {
        _host = host;
        _rootServiceProvider = rootServiceProvider;
        _hooks = [.. hooks.OrderBy(hook => hook.Order)];
        _testIdGenerator = testIdGenerator;
        _trace = trace;
    }

    public static ProtoExecutionContext CurrentContext => Current.Value?.Context
        ?? throw new InvalidOperationException("No active ProtoExecutionContext available on this thread.");

    /// <summary>Gets the current test context, or <see langword="null"/> when none is active on this flow.</summary>
    public static ProtoExecutionContext? TryGetCurrentContext => Current.Value?.Context;

    public static ProtoHost? CurrentHost => Current.Value?.Host;

    public Task<ProtoExecutionContext> StartAsync(
        string testName,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes,
        IProtoTestAttachmentPublisher? attachmentPublisher)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        return StartAsync(testName, _testIdGenerator.Next(testMethod), testMethod, attributes, attachmentPublisher);
    }

    public Task<ProtoExecutionContext> StartAsync(
        string testName,
        ProtoTestId testId,
        MethodInfo testMethod,
        IEnumerable<ProtoAttribute>? attributes,
        IProtoTestAttachmentPublisher? attachmentPublisher)
    {
        if (Current.Value?.Context is not null)
        {
            throw new InvalidOperationException(
                "A ProtoExecutionContext is already active on this async flow. Complete the active test before starting another one.");
        }

        ArgumentNullException.ThrowIfNull(testName);
        ArgumentNullException.ThrowIfNull(testMethod);

        // This method stays synchronous so the ambient AsyncLocal context it sets is visible to the
        // caller's execution context; awaiting here would scope the change to this state machine.
        var scope = _rootServiceProvider.CreateScope();
        ContextState state;
        try
        {
            var testTrace = _trace.StartTest(testName, testId, testMethod);
            var context = new ProtoExecutionContext(testName, scope, testId, testMethod, testTrace);
            state = new ContextState(
                _host,
                context,
                attributes?.OrderBy(attribute => attribute.Order).ToArray() ?? [],
                attachmentPublisher);
        }
        catch
        {
            // Execution has not begun, so no teardown will dispose the scope: release it here.
            LifecycleExceptionHelper.DisposeOrSync(scope);
            throw;
        }

        Current.Value = state;
        return ExecuteBeforeAsync(state);
    }

    public async Task CompleteAsync(ProtoTestResult result)
    {
        var state = Current.Value;
        if (state?.Context is null)
        {
            return;
        }

        if (!ReferenceEquals(state.Host, _host))
        {
            throw new InvalidOperationException("The active test context belongs to a different ProtoHost.");
        }

        var exceptions = new List<Exception>();
        await TeardownAsync(state, exceptions, result, isRollback: false);
        LifecycleExceptionHelper.ThrowIfAny(
            "One or more test lifecycle components failed during teardown.", exceptions);
    }

    private async Task<ProtoExecutionContext> ExecuteBeforeAsync(ContextState state)
    {
        state.SetupOperation = state.Context!.Trace
            .Operation("test.setup", "Setup", "ProtoTest.Core")
            .During(ProtoTracePhase.Setup)
            .With("hook.count", _hooks.Count.ToString())
            .With("attribute.count", state.Attributes.Count.ToString())
            .With("test.class", state.Context.TestMethod.DeclaringType?.FullName)
            .With("test.method", state.Context.TestMethod.Name)
            .Begin();
        try
        {
            foreach (var hook in _hooks)
            {
                await RunSetupStepAsync(
                    state.Context!,
                    "hook.before",
                    $"Before · {hook.GetType().Name}",
                    new Dictionary<string, string?>
                    {
                        ["hook.type"] = hook.GetType().FullName,
                        ["hook.order"] = hook.Order.ToString()
                    },
                    () => hook.BeforeTestAsync(state.Context!),
                    () => state.CompletedHooks.Add(hook));
            }

            foreach (var attribute in state.Attributes)
            {
                await RunSetupStepAsync(
                    state.Context!,
                    "attribute.before",
                    $"Before · {attribute.GetType().Name}",
                    new Dictionary<string, string?>
                    {
                        ["attribute.type"] = attribute.GetType().FullName,
                        ["attribute.order"] = attribute.Order.ToString()
                    },
                    () => attribute.BeforeTestAsync(state.Context!),
                    () => state.CompletedAttributes.Add(attribute));
            }

            state.SetupOperation.Succeed();
            state.SetupOperation.Dispose();
            state.ExecutionOperation = state.Context!.Trace
                .Operation("test.execution", "Test execution", "ProtoTest.Core")
                .During(ProtoTracePhase.Execution)
                .With("test.class", state.Context.TestMethod.DeclaringType?.FullName)
                .With("test.method", state.Context.TestMethod.Name)
                .Begin();
            if (state.Context.Trace is ProtoTestTraceRecorder recorder)
            {
                recorder.SetDefaultParent(state.ExecutionOperation.Id);
            }
            return state.Context!;
        }
        catch (Exception exception)
        {
            state.SetupOperation?.Fail(exception);
            state.SetupOperation?.Dispose();
            var exceptions = new List<Exception> { exception };
            await TeardownAsync(
                state,
                exceptions,
                ProtoTestResult.Failed(exception),
                isRollback: true);
            LifecycleExceptionHelper.ThrowIfAny(
                "Test setup failed and completed lifecycle components were rolled back.", exceptions);
            throw;
        }
    }

    private static async Task RunSetupStepAsync(
        ProtoExecutionContext context,
        string kind,
        string name,
        IReadOnlyDictionary<string, string?> attributes,
        Func<Task> step,
        Action onCompleted)
        => await context.Trace
            .Operation(kind, name, "ProtoTest.Core")
            .During(ProtoTracePhase.Setup)
            .With(attributes)
            .RunAsync(async _ =>
            {
                await step();
                onCompleted();
            });

    private static async Task TeardownAsync(
        ContextState state,
        List<Exception> exceptions,
        ProtoTestResult result,
        bool isRollback)
    {
        var context = state.Context!;
        var phase = isRollback ? ProtoTracePhase.Rollback : ProtoTracePhase.Teardown;
        var recorder = context.Trace as ProtoTestTraceRecorder;
        state.ExecutionOperation?.Complete(result);
        state.ExecutionOperation?.Dispose();
        recorder?.SetDefaultParent(null);

        using var lifecycleOperation = context.Trace
            .Operation(
                isRollback ? "test.rollback" : "test.teardown",
                isRollback ? "Rollback" : "Teardown",
                "ProtoTest.Core")
            .During(phase)
            .With("hook.completed_count", state.CompletedHooks.Count.ToString())
            .With("attribute.completed_count", state.CompletedAttributes.Count.ToString())
            .With("attachment.count", context.Attachments.Count.ToString())
            .With("test.outcome", result.Outcome.ToString())
            .Begin();
        var exceptionCountBeforeTeardown = exceptions.Count;

        // The teardown chain is a collect-mode flow: every step runs, each keeps its own trace
        // operation, and the failures come back as a list instead of being threaded through the loops.
        var flow = new ProtoFlow(
            isRollback ? "test.rollback" : "test.teardown",
            "ProtoTest.Core",
            ProtoFlowFailureMode.Collect);

        foreach (var attribute in state.CompletedAttributes.AsEnumerable().Reverse())
        {
            var current = attribute;
            flow.Step(
                new ProtoStepDescriptor(
                    "attribute.after",
                    $"After · {current.GetType().Name}",
                    "ProtoTest.Core",
                    phase,
                    new Dictionary<string, string?>
                    {
                        ["attribute.type"] = current.GetType().FullName,
                        ["attribute.order"] = current.Order.ToString()
                    }),
                _ => new ValueTask(current.AfterTestAsync(context)));
        }

        foreach (var hook in state.CompletedHooks.AsEnumerable().Reverse())
        {
            var current = hook;
            flow.Step(
                new ProtoStepDescriptor(
                    "hook.after",
                    $"After · {current.GetType().Name}",
                    "ProtoTest.Core",
                    phase,
                    new Dictionary<string, string?>
                    {
                        ["hook.type"] = current.GetType().FullName,
                        ["hook.order"] = current.Order.ToString()
                    }),
                _ => new ValueTask(current.AfterTestAsync(context)));
        }

        exceptions.AddRange((await flow.RunAsync(context.Trace)).Failures);

        // Publishing and disposal run in a second flow: a teardown hook may add an attachment, and
        // the attachment list must be snapshotted after those hooks, not when the first flow was built.
        var releaseFlow = new ProtoFlow(
            isRollback ? "test.rollback" : "test.teardown",
            "ProtoTest.Core",
            ProtoFlowFailureMode.Collect);
        if (state.AttachmentPublisher is { } publisher)
        {
            foreach (var attachment in context.Attachments)
            {
                var current = attachment;
                releaseFlow.Step(
                    new ProtoStepDescriptor(
                        "attachment.publish",
                        $"Publish · {current.Name}",
                        "ProtoTest.Core",
                        phase,
                        new Dictionary<string, string?>
                        {
                            ["attachment.name"] = current.Name,
                            ["attachment.media_type"] = current.MediaType,
                            ["attachment.description"] = current.Description,
                            ["attachment.source"] = current.IsFile ? "file" : "memory"
                        }),
                    _ => publisher.PublishAsync(current));
            }
        }

        releaseFlow.Step(
            new ProtoStepDescriptor(
                "context.dispose",
                "Dispose execution context",
                "ProtoTest.Core",
                phase,
                new Dictionary<string, string?>
                {
                    ["context.type"] = context.GetType().FullName,
                    ["attachment.count"] = context.Attachments.Count.ToString()
                }),
            _ => context.DisposeAsync(phase));

        exceptions.AddRange((await releaseFlow.RunAsync(context.Trace)).Failures);

        // Capturing artifacts is one teardown step among several: its failure is recorded like any
        // other, but it never replaces the result the test reported. Completing the test still runs
        // after it, so the ambient context is always cleared.
        if (recorder is not null)
        {
            try
            {
                await recorder.CaptureArtifactsAsync(context.Attachments);
            }
            catch (Exception exception)
            {
                exceptions.Add(exception);
            }
        }

        if (exceptions.Count > exceptionCountBeforeTeardown)
        {
            // The teardown exception is evidence, not a replacement for the result the test reported:
            // the original outcome stands, so a failed assertion is not hidden by a cleanup error. The
            // failing teardown operation and the findings below carry the teardown error instead.
            lifecycleOperation.Fail(exceptions[^1]);
            foreach (var failure in exceptions.Skip(exceptionCountBeforeTeardown))
            {
                // The same path as any other finding, so a teardown failure reaches the sinks and run
                // gates instead of living only in the trace.
                context.AddFinding(
                    $"Teardown failed: {failure.Message}",
                    ProtoReportStatus.Error,
                    category: "Teardown",
                    targetName: context.TestName,
                    tags: [failure.GetType().Name]);
            }
        }
        else
        {
            lifecycleOperation.Succeed();
        }
        lifecycleOperation.Dispose();

        recorder?.CompleteTest(result);

        if (ReferenceEquals(Current.Value, state))
        {
            state.Context = null;
            Current.Value = null;
        }
    }

    private sealed class ContextState(
        ProtoHost host,
        ProtoExecutionContext context,
        IReadOnlyList<ProtoAttribute> attributes,
        IProtoTestAttachmentPublisher? attachmentPublisher)
    {
        public ProtoHost Host { get; } = host;
        public ProtoExecutionContext? Context { get; set; } = context;
        public IReadOnlyList<ProtoAttribute> Attributes { get; } = attributes;
        public IProtoTestAttachmentPublisher? AttachmentPublisher { get; } = attachmentPublisher;
        public List<IProtoTestHook> CompletedHooks { get; } = [];
        public List<ProtoAttribute> CompletedAttributes { get; } = [];
        public ProtoTraceOperation? SetupOperation { get; set; }
        public ProtoTraceOperation? ExecutionOperation { get; set; }
    }
}
