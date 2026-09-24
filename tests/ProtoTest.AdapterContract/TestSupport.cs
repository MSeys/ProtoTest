namespace ProtoTest.AdapterContract;

using System.Collections.Concurrent;
using ProtoTest.Core;

/// <summary>Test support every adapter test project shares: the tracking hook and attribute, the
/// context attribute, and the service the adapter's DI registration is proved with.</summary>
public interface ITestService
{
    /// <summary>Returns the adapter-specific probe message.</summary>
    string GetMessage();
}

/// <summary>
/// The probe service every adapter's DI registration is proved with; each adapter supplies its own
/// message, so the class lives once instead of in five projects.
/// </summary>
public sealed class ProbeTestService(string message) : ITestService
{
    public string GetMessage() => message;
}

/// <summary>The per-test lifecycle log the tracking hook and attribute write to.</summary>
public sealed class ExecutionLogState : IProtoContext
{
    public List<string> Log { get; } = [];

    /// <summary>Whether a compliance test body ran <see cref="AdapterLifecycle.VerifyTestBody{T}"/>.</summary>
    internal bool ContractVerified { get; set; }
}

/// <summary>
/// Switchboard for the shared failure probes. A driver registers the lifecycle method's name before
/// invoking the adapter's real entry point, and <see cref="FailureProbeHook"/> fails the matching
/// lifecycle phase so the adapter's setup/teardown failure handling can be characterized without a red
/// suite. The registration is process-local and removed when the driver finishes.
/// </summary>
public static class AdapterFailureProbe
{
    public const string SetupMessage = "The setup probe failed.";
    public const string TeardownMessage = "The teardown probe failed.";

    private static readonly ConcurrentDictionary<string, ProbePhase> Phases = new(StringComparer.Ordinal);

    /// <summary>Fails setup for the next lifecycle whose method has this name.</summary>
    public static IDisposable BeginSetupFailure(string testMethodName)
    {
        Phases[testMethodName] = ProbePhase.Setup;
        return new ProbeScope(testMethodName);
    }

    /// <summary>Fails teardown for the next lifecycle whose method has this name.</summary>
    public static IDisposable BeginTeardownFailure(string testMethodName)
    {
        Phases[testMethodName] = ProbePhase.Teardown;
        return new ProbeScope(testMethodName);
    }

    internal static bool ShouldFail(string testMethodName, ProbePhase phase)
        => Phases.TryGetValue(testMethodName, out var registered) && registered == phase;

    internal enum ProbePhase
    {
        Setup,
        Teardown
    }

    private sealed class ProbeScope(string testMethodName) : IDisposable
    {
        public void Dispose() => Phases.TryRemove(testMethodName, out _);
    }
}

/// <summary>Fails setup or teardown for the method names registered through <see cref="AdapterFailureProbe"/>.</summary>
internal sealed class FailureProbeHook : IProtoTestHook
{
    public int Order => 1;

    public Task BeforeTestAsync(ProtoExecutionContext context)
        => AdapterFailureProbe.ShouldFail(context.TestMethod.Name, AdapterFailureProbe.ProbePhase.Setup)
            ? Task.FromException(new InvalidOperationException(AdapterFailureProbe.SetupMessage))
            : Task.CompletedTask;

    public Task AfterTestAsync(ProtoExecutionContext context)
        => AdapterFailureProbe.ShouldFail(context.TestMethod.Name, AdapterFailureProbe.ProbePhase.Teardown)
            ? Task.FromException(new InvalidOperationException(AdapterFailureProbe.TeardownMessage))
            : Task.CompletedTask;
}

/// <summary>
/// Verifies the completed run once every test finished, while the host is still active. Registered by
/// <see cref="AdapterLifecycle.ConfigureHost"/>, so every adapter test project fails loudly when an
/// adapter records no outcome or does not complete the shared compliance test.
/// </summary>
internal sealed class RunContractVerificationHook : IProtoRunHook
{
    public Task BeforeRunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task AfterRunAsync(CancellationToken cancellationToken = default)
    {
        AdapterLifecycle.VerifyCompletedRun(ProtoHost.CurrentHost);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Records the hook stage of the lifecycle order every adapter must preserve, and validates the
/// completed sequence at teardown for a test that verified its body through
/// <see cref="AdapterLifecycle.VerifyTestBody{T}"/>.
/// </summary>
public sealed class TrackingHook : IProtoTestHook
{
    public int Order => 1;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        GetOrCreateLog(context).Add("Hook:Before");
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
    {
        var state = context.TryResolve<ExecutionLogState>();
        if (state is null || !state.ContractVerified)
        {
            return Task.CompletedTask;
        }

        state.Log.Add("Hook:After");
        AdapterLifecycle.Ensure(
            state.Log.SequenceEqual(AdapterLifecycle.ExpectedCompletedSequence),
            $"Unexpected completed lifecycle: {string.Join(", ", state.Log)}");
        return Task.CompletedTask;
    }

    /// <summary>Returns the test's log, creating the state when the hook has not run yet.</summary>
    public static List<string> GetOrCreateLog(ProtoExecutionContext context)
    {
        var state = context.TryResolve<ExecutionLogState>();
        if (state is null)
        {
            state = new ExecutionLogState();
            context.SetContext(state);
        }

        return state.Log;
    }
}

/// <summary>
/// Records an attribute stage of the lifecycle order, before and after the test body, and proves the
/// attribute instance is not recreated between setup and teardown.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class TrackingAttribute(string name) : ProtoAttribute
{
    private bool _beforeRan;

    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        _beforeRan = true;
        TrackingHook.GetOrCreateLog(context).Add($"{name}:Before");
        return Task.CompletedTask;
    }

    public override Task AfterTestAsync(ProtoExecutionContext context)
    {
        AdapterLifecycle.Ensure(_beforeRan,
            $"The '{name}' attribute instance was recreated between setup and teardown.");
        TrackingHook.GetOrCreateLog(context).Add($"{name}:After");
        return Task.CompletedTask;
    }
}

/// <summary>Sets test-scoped context before the test runs, proving attribute-to-context wiring.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SetContextUserAttribute(string username) : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        context.SetContext(new UserState(username));
        return Task.CompletedTask;
    }
}

/// <summary>The state <see cref="SetContextUserAttribute"/> installs.</summary>
public sealed record UserState(string Username) : IProtoContext;
