namespace ProtoTest.AspNetCore;

using ProtoTest.Core;

/// <summary>
/// Substitutes a service in the application under test for one test. The test runs against a
/// dedicated server built with the replacement before it starts, so the run's shared server never
/// sees the substitution and the next test starts clean. The test skips when the application is
/// not hosted in-process, mirroring <see cref="RequiresInProcessAttribute"/>.
/// </summary>
/// <typeparam name="TService">The service contract the application resolves.</typeparam>
/// <example>
/// <code>
/// [ReplaceService&lt;ITestMessageService&gt;(typeof(StubMessageService))]
/// public async Task Substituting_a_service_serves_the_replacement() { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class ReplaceServiceAttribute<TService> : ProtoAttribute, IProtoApplicationSkipCondition
    where TService : class
{
    public ReplaceServiceAttribute(Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(implementationType);
        if (!typeof(TService).IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"The replacement '{implementationType.FullName}' cannot serve '{typeof(TService).FullName}'.",
                nameof(implementationType));
        }

        if (implementationType.IsInterface || implementationType.IsAbstract)
        {
            throw new ArgumentException(
                $"The replacement '{implementationType.FullName}' must be a concrete type the container can construct.",
                nameof(implementationType));
        }

        ImplementationType = implementationType;
    }

    /// <summary>The concrete replacement the container constructs for the test.</summary>
    public Type ImplementationType { get; }

    /// <summary>
    /// The server to substitute on. When omitted, the test's selected application is used, falling
    /// back to <c>Default</c> - the same rule the server accessors resolve by.
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
    /// Gates on the same target the substitution resolves: an explicit <see cref="Server"/> needs that
    /// named in-process server, otherwise the test's selected application (falling back to
    /// <c>Default</c>) must be backed by an in-process server. A run where the selected application is
    /// published but another server is live skips instead of failing when the substitution resolves.
    /// </summary>
    private string? GetSkipReason(ProtoHost host, string? applicationName)
    {
        if (!string.IsNullOrWhiteSpace(Server))
        {
            return host.HasCapability(ProtoCapabilityKinds.Server, null, Server)
                ? null
                : Reason
                    ?? host.FindCapabilityReason(ProtoCapabilityKinds.Server, Server)
                    ?? $"This test substitutes '{typeof(TService).Name}' on the '{Server}' server, which this run does not host in-process. " +
                       $"Register it with AddAspNetCoreServer(\"{Server}\").";
        }

        var application = string.IsNullOrWhiteSpace(applicationName) ? "Default" : applicationName;
        return host.HasCapability(ProtoCapabilityKinds.Server, null, application)
            ? null
            : Reason
                ?? host.FindCapabilityReason(ProtoCapabilityKinds.Server, application)
                ?? $"This test substitutes '{typeof(TService).Name}' on the '{application}' application, which this run does not host in-process. " +
                   "Register it with AddAspNetCoreServer, or name the in-process server in a mixed run.";
    }

    public override Task BeforeTestAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var serverName = AspNetCoreSubstitution.ResolveServerName(context, Server);
        var target = AspNetCoreSubstitution.ResolveTarget(context, serverName);
        target.ApplySubstitution(
            context,
            new TypeSubstitution(typeof(TService), serverName, ImplementationType),
            ProtoTracePhase.Setup);
        return Task.CompletedTask;
    }
}
