namespace ProtoTest.Core;

/// <summary>
/// A condition that can stop a test before its lifecycle starts, with a reason the runner can report.
/// Adapters evaluate these before starting the test; a runner that does not evaluate them simply runs
/// the test, so the contract stays opt-in.
/// </summary>
public interface IProtoSkipCondition
{
    /// <summary>Returns the reason to skip, or <see langword="null"/> when the test can run.</summary>
    string? GetSkipReason(ProtoHost host);
}

/// <summary>
/// A skip condition whose answer depends on the application the test selected with
/// <see cref="ApplicationAttribute"/>. The runner resolves that selection from the same attribute set
/// it is about to run and passes the application name here, so a condition that targets an application
/// can skip honestly in a run where another application is live. A condition that ignores the
/// selection implements <see cref="IProtoSkipCondition"/> only.
/// </summary>
public interface IProtoApplicationSkipCondition : IProtoSkipCondition
{
    /// <summary>
    /// Returns the reason to skip for the selected application (or <see langword="null"/> when the
    /// test selected none), or <see langword="null"/> when the test can run.
    /// </summary>
    string? GetSkipReason(ProtoHost host, string? applicationName);
}

/// <summary>Evaluates the skip conditions of a resolved attribute set.</summary>
public static class ProtoTestSkip
{
    /// <summary>Returns the first skip reason, or <see langword="null"/> when no condition applies.</summary>
    public static string? GetReason(IEnumerable<ProtoAttribute> attributes, ProtoHost host)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(host);
        var resolved = attributes as IReadOnlyList<ProtoAttribute> ?? [.. attributes];
        // The lifecycle runs a class-level selection before a method-level one, so the last
        // [Application] in the resolved set is the one the test runs under.
        var applicationName = resolved.OfType<ApplicationAttribute>().LastOrDefault()?.Name;
        return resolved
            .OfType<IProtoSkipCondition>()
            .Select(condition => condition is IProtoApplicationSkipCondition applicationCondition
                ? applicationCondition.GetSkipReason(host, applicationName)
                : condition.GetSkipReason(host))
            .FirstOrDefault(reason => reason is not null);
    }
}

/// <summary>
/// Skips the test unless the host is composed with a capability of the given kind - for example a
/// test-side domain that only exists when the suite owns the store. The kind is open, like capability
/// kinds themselves; <see cref="ProtoCapabilityKinds"/> lists the built-in ones.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public class RequiresCapabilityAttribute : ProtoAttribute, IProtoSkipCondition
{
    public RequiresCapabilityAttribute(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
    }

    /// <summary>The capability kind the test needs.</summary>
    public string Kind { get; }

    /// <summary>The capability name within the kind, when a specific one is required.</summary>
    public string? CapabilityName { get; init; }

    /// <summary>
    /// The instance within the capability when a specific one is required - the name of an
    /// <c>AddAspNetCoreServer</c> server, an application an in-process device transport belongs to
    /// (<see cref="ProtoCapabilityDescriptor.Instance"/>). <see cref="CapabilityName"/> and this filter
    /// compose: every non-null filter must match.
    /// </summary>
    public string? CapabilityInstance { get; init; }

    /// <summary>The reason reported when the capability is missing.</summary>
    public string? Reason { get; init; }

    public string? GetSkipReason(ProtoHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (host.HasCapability(Kind, CapabilityName, CapabilityInstance))
        {
            return null;
        }

        // A per-test reason wins over the suite-level one, which wins over this attribute's default.
        return Reason
            ?? host.FindCapabilityReason(Kind, CapabilityName ?? CapabilityInstance)
            ?? DefaultReason;
    }

    protected virtual string DefaultReason
        => $"This test requires the '{CapabilityInstance ?? CapabilityName ?? Kind}' capability, which this host is not composed with.";
}

/// <summary>
/// Skips the test unless the application runs in-process: in-process services, transactional isolation
/// and a test-side composition of the application's own code only exist in that hosting mode.
/// </summary>
public sealed class RequiresInProcessAttribute : RequiresCapabilityAttribute
{
    public RequiresInProcessAttribute() : base(ProtoCapabilityKinds.Server)
    {
    }

    protected override string DefaultReason
        => "This test requires an in-process application server; the suite is running against a published environment.";
}
