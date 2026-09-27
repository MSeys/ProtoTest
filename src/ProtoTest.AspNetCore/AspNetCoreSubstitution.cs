namespace ProtoTest.AspNetCore;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

/// <summary>
/// The per-server seam a substitution applies through. The generic server initializer behind a
/// server name owns the application's web-host configuration, so substitutions reach the same
/// combined configuration (infrastructure settings, the run's clock bridge, the suite's callback)
/// the shared server is built with.
/// </summary>
internal interface IAspNetCoreSubstitutionTarget
{
    /// <summary>The server name this target builds.</summary>
    string ServerName { get; }

    /// <summary>
    /// Adds the substitution to the test's union for the server and rebuilds the test's dedicated
    /// server with it, unless the union is already applied.
    /// </summary>
    void ApplySubstitution(ProtoExecutionContext context, ServiceSubstitution substitution, ProtoTracePhase phase);
}

/// <summary>
/// Accumulates one test's substitutions per server, so a class-level attribute, a method-level
/// attribute and a body <c>Override</c> compose into one dedicated server built with their union
/// instead of one server each. State is keyed by the test's own context and dies with it: nothing
/// is shared between tests, so parallel tests that substitute differently never meet.
/// </summary>
internal static class AspNetCoreSubstitutionLedger
{
    private static readonly ConditionalWeakTable<ProtoExecutionContext, SubstitutionStates> States = new();

    /// <summary>
    /// Adds the substitution to the test's union for its server. Returns the union to build, or
    /// <see langword="null"/> when that union is already applied and no rebuild is needed.
    /// </summary>
    internal static IReadOnlyList<ServiceSubstitution>? Append(
        ProtoExecutionContext context,
        ServiceSubstitution substitution)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(substitution);
        var states = States.GetValue(context, static _ => new SubstitutionStates());
        lock (states.Gate)
        {
            if (!states.Servers.TryGetValue(substitution.ServerName, out var state))
            {
                state = new ServerSubstitutions();
                states.Servers[substitution.ServerName] = state;
            }

            state.Records.Add(substitution);
            var union = state.Records.ToArray();
            return Signature(union) == state.AppliedSignature ? null : union;
        }
    }

    /// <summary>Records that the union now serves the test, so reapplying it is a no-op.</summary>
    internal static void MarkApplied(
        ProtoExecutionContext context,
        string serverName,
        IReadOnlyList<ServiceSubstitution> union)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentNullException.ThrowIfNull(union);
        var states = States.GetValue(context, static _ => new SubstitutionStates());
        lock (states.Gate)
        {
            if (!states.Servers.TryGetValue(serverName, out var state))
            {
                state = new ServerSubstitutions();
                states.Servers[serverName] = state;
            }

            state.AppliedSignature = Signature(union);
        }
    }

    private static string Signature(IReadOnlyList<ServiceSubstitution> union)
        => string.Join(";", union.Select(record => $"{record.OperationKind}:{record.ServiceType.FullName}:{record.Signature}"));

    private sealed class SubstitutionStates
    {
        public readonly ProtoLock Gate = new();
        public readonly Dictionary<string, ServerSubstitutions> Servers = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ServerSubstitutions
    {
        public readonly List<ServiceSubstitution> Records = [];
        public string? AppliedSignature;
    }
}

/// <summary>Resolves which server a substitution targets and the target that builds it.</summary>
internal static class AspNetCoreSubstitution
{
    /// <summary>
    /// Resolves the server name: the explicit name, then the test's selected application, then
    /// <c>Default</c> - the same rule the server accessors resolve by.
    /// </summary>
    internal static string ResolveServerName(ProtoExecutionContext context, string? name)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (name is not null && string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "The server name must not be blank. Omit it to target the test's application, or pass the AddAspNetCoreServer name.",
                nameof(name));
        }

        return !string.IsNullOrWhiteSpace(name)
            ? name
            : context.TryResolve<ProtoApplicationState>()?.ApplicationName ?? "Default";
    }

    /// <summary>Finds the substitution target behind a server name, or fails naming the registration.</summary>
    internal static IAspNetCoreSubstitutionTarget ResolveTarget(ProtoExecutionContext context, string serverName)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        var target = context.Services.GetServices<IProtoClientInitializer>()
            .OfType<IAspNetCoreSubstitutionTarget>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.ServerName, serverName, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            throw new InvalidOperationException(
                $"No in-process ASP.NET Core server is registered under '{serverName}', so its services cannot be substituted. " +
                $"Register one with AddAspNetCoreServer<TProgram>(\"{serverName}\").");
        }

        return target;
    }
}
