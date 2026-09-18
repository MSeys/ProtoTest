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

/// <summary>Evaluates the skip conditions of a resolved attribute set.</summary>
public static class ProtoTestSkip
{
    /// <summary>Returns the first skip reason, or <see langword="null"/> when no condition applies.</summary>
    public static string? GetReason(IEnumerable<ProtoAttribute> attributes, ProtoHost host)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(host);
        return attributes
            .OfType<IProtoSkipCondition>()
            .Select(condition => condition.GetSkipReason(host))
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

    /// <summary>The reason reported when the capability is missing.</summary>
    public string? Reason { get; init; }

    public string? GetSkipReason(ProtoHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.HasCapability(Kind, CapabilityName) ? null : Reason ?? DefaultReason;
    }

    protected virtual string DefaultReason
        => $"This test requires the '{CapabilityName ?? Kind}' capability, which this host is not composed with.";
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
        => Reason ?? "This test requires an in-process application server; the suite is running against a published environment.";
}
