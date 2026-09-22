namespace ProtoTest.Core;

using System.Reflection;

/// <summary>
/// A test method resolved once for execution: the attributes the host orders hooks from, the name the
/// trace records, and the reason the test must skip, if any of the run's skip conditions apply.
/// </summary>
public sealed record ProtoTestPreparation(
    MethodInfo Method,
    string TestName,
    IReadOnlyList<ProtoAttribute> Attributes,
    string? SkipReason)
{
    /// <summary>Whether the test can run; when false, <see cref="SkipReason"/> explains why.</summary>
    public bool CanRun => SkipReason is null;

    /// <summary>
    /// Starts the test lifecycle. Adapters raise their framework's own skip; this is everything else.
    /// </summary>
    public Task<ProtoExecutionContext> StartAsync(
        ProtoHost host,
        IProtoTestAttachmentPublisher? attachmentPublisher = null)
        => host.StartTestAsync(TestName, Method, Attributes, attachmentPublisher);
}

/// <summary>
/// The preparation every runner adapter shares: resolve the attributes that apply to a test method,
/// ask the run's skip conditions, then start the lifecycle. Keeping it in one place is what makes the
/// five adapters behave the same - including skipping before the lifecycle starts, so the trace stays
/// honest: nothing ran, nothing failed.
/// </summary>
public static class ProtoTestAdapter
{
    /// <summary>
    /// Prepares a test method for execution. The name defaults to the stable fully qualified name;
    /// adapters whose framework reports a richer one (a theory row's display name, for example) pass it.
    /// </summary>
    public static ProtoTestPreparation Prepare(MethodInfo method, ProtoHost host, string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(host);

        var attributes = ProtoAttributeResolver.Resolve(method);
        return new ProtoTestPreparation(
            method,
            displayName ?? ProtoTestName.FromMethod(method),
            attributes,
            ProtoTestSkip.GetReason(attributes, host));
    }
}
