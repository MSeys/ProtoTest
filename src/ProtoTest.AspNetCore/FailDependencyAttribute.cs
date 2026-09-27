namespace ProtoTest.AspNetCore;

using ProtoTest.Core;

/// <summary>
/// Fails a dependency of the application under test for one test, for the error paths: resolving
/// the service throws instead of serving an instance. The test runs against a dedicated server
/// built with the failure before it starts, so the run's shared server never sees it and the next
/// test starts clean. The test skips when the application is not hosted in-process, mirroring
/// <see cref="RequiresInProcessAttribute"/>.
/// </summary>
/// <typeparam name="TService">The service contract the application resolves.</typeparam>
/// <example>
/// <code>
/// [FailDependency&lt;ITestMessageService&gt;]
/// public async Task A_failed_dependency_answers_500() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class FailDependencyAttribute<TService> : ProtoAttribute, IProtoApplicationSkipCondition
    where TService : class
{
    /// <summary>
    /// The server to fail the dependency on. When omitted, the test's selected application is used,
    /// falling back to <c>Default</c> - the same rule the server accessors resolve by.
    /// </summary>
    public string? Server { get; init; }

    /// <summary>The reason reported when the test skips.</summary>
    public string? Reason { get; init; }

    public string? GetSkipReason(ProtoHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return GetSkipReason(host, applicationName: null);
    }

    string? IProtoApplicationSkipCondition.GetSkipReason(ProtoHost host, string? applicationName)
    {
        ArgumentNullException.ThrowIfNull(host);
        return GetSkipReason(host, applicationName);
    }

    /// <summary>
    /// Gates on the same target the dependency failure resolves: an explicit <see cref="Server"/> needs
    /// that named in-process server, otherwise the test's selected application (falling back to
    /// <c>Default</c>) must be backed by an in-process server.
    /// </summary>
    private string? GetSkipReason(ProtoHost host, string? applicationName)
    {
        if (!string.IsNullOrWhiteSpace(Server))
        {
            return host.HasCapability(ProtoCapabilityKinds.Server, null, Server)
                ? null
                : Reason
                    ?? host.FindCapabilityReason(ProtoCapabilityKinds.Server, Server)
                    ?? $"This test fails '{typeof(TService).Name}' on the '{Server}' server, which this run does not host in-process. " +
                       $"Register it with AddAspNetCoreServer(\"{Server}\").";
        }

        var application = string.IsNullOrWhiteSpace(applicationName) ? "Default" : applicationName;
        return host.HasCapability(ProtoCapabilityKinds.Server, null, application)
            ? null
            : Reason
                ?? host.FindCapabilityReason(ProtoCapabilityKinds.Server, application)
                ?? $"This test fails '{typeof(TService).Name}' on the '{application}' application, which this run does not host in-process. " +
                   "Register it with AddAspNetCoreServer, or name the in-process server in a mixed run.";
    }

    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var serverName = AspNetCoreSubstitution.ResolveServerName(context, Server);
        var target = AspNetCoreSubstitution.ResolveTarget(context, serverName);
        target.ApplySubstitution(
            context,
            new FailSubstitution(typeof(TService), serverName),
            ProtoTracePhase.Setup);
        return Task.CompletedTask;
    }
}
