namespace ProtoTest.AdapterContract;

using System.Reflection;
using ProtoTest.Core;

/// <summary>
/// Test support every adapter test project shares: the lifecycle log hook and attribute, the context
/// attribute, and the service the adapter's DI registration is proved with. One copy keeps the
/// cross-adapter comparison meaningful and stops the copies from drifting.
/// </summary>
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

/// <summary>
/// Shared lifecycle expectations and lookups. Named apart from <see cref="AdapterContract"/> because a
/// test namespace such as <c>ProtoTest.Xunit.Tests</c> resolves the qualified name to the enclosing
/// <c>ProtoTest.AdapterContract</c> namespace first.
/// </summary>
public static class AdapterTestSupport
{
    /// <summary>The lifecycle events every adapter must record before the test body runs, as logged by
    /// <see cref="TrackingAttribute"/> and <see cref="TrackingHook"/>.</summary>
    public static readonly string[] ExpectedBeforeSequence =
    [
        "Hook:Before",
        "ClassLevel:Before",
        "MethodLevel:Before"
    ];

    /// <summary>Registers the hooks every adapter test project shares.</summary>
    public static void ConfigureHost(IProtoHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddTestHook<TrackingHook>();
        builder.AddTestHook<AdapterContractHook>();
    }

    /// <summary>Returns the trace of the most recent run of <paramref name="method"/> on the host.</summary>
    public static ProtoTestTrace TraceFor(ProtoHost host, MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(method);
        var name = ProtoTestName.FromMethod(method);
        return host.Trace.Snapshot().Tests.Last(test => test.Name == name);
    }
}

/// <summary>The per-test lifecycle log the tracking hook and attribute write to.</summary>
public sealed class ExecutionLogState : IProtoContext
{
    public List<string> Log { get; } = [];
}

/// <summary>Records the hook stage of the lifecycle order every adapter must preserve.</summary>
public sealed class TrackingHook : IProtoTestHook
{
    public int Order => 1;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        GetOrCreateLog(context).Add("Hook:Before");
        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

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

/// <summary>Records an attribute stage of the lifecycle order, before and after the test body.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class TrackingAttribute(string name) : ProtoAttribute
{
    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        context.Resolve<ExecutionLogState>().Log.Add($"{name}:Before");
        return Task.CompletedTask;
    }

    public override Task AfterTestAsync(ProtoExecutionContext context)
    {
        context.Resolve<ExecutionLogState>().Log.Add($"{name}:After");
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
