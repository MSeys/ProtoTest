namespace ProtoTest.Core;

/// <summary>
/// An attribute that declares the attributes it groups, so one declaration a test carries stands for
/// several. The framework expands a composite wherever attributes are resolved: the composed
/// attributes run like declarations the test made itself - at their own <see cref="ProtoAttribute.Order"/> -
/// and the composite's own lifecycle behavior runs at its order too. A composite may compose another
/// composite; a cycle is rejected when the attributes are resolved.
/// </summary>
/// <example>
/// <code>
/// [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
/// public sealed class NorthstarMemberAttribute(string planId = PlanIds.Free) : ProtoCompositeAttribute
/// {
///     protected override IReadOnlyList&lt;Attribute&gt; Compose() =&gt;
///     [
///         new NorthstarTenantAttribute(planId),
///         new AuthAttribute&lt;NorthstarAuthenticator&gt;(),
///     ];
/// }
/// </code>
/// </example>
/// <remarks>
/// <see cref="Compose"/> returns the instances that run, so it must be a deterministic factory and is
/// called once per attribute instance. Composition is by attribute instance, not by inheritance: a
/// composite and the attributes it declares are ordinary attributes that happen to be resolved
/// together.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public abstract class ProtoCompositeAttribute : ProtoAttribute
{
    private IReadOnlyList<Attribute>? _composed;

    /// <summary>
    /// Returns the attributes this composite declares. The instances returned are the ones the
    /// framework runs; an instance that is also declared explicitly on the test runs once, and the
    /// explicit declaration wins.
    /// </summary>
    protected abstract IReadOnlyList<Attribute> Compose();

    /// <summary>The composed instances, materialized once so the trace names what actually ran.</summary>
    internal IReadOnlyList<Attribute> ComposedAttributes => _composed ??= Materialize();

    private IReadOnlyList<Attribute> Materialize()
    {
        var composed = Compose()
            ?? throw new InvalidOperationException(
                $"'{GetType().FullName}.Compose()' returned null. Return the attributes this composite " +
                "declares, or an empty list when it declares none.");

        foreach (var attribute in composed)
        {
            if (attribute is null)
            {
                throw new InvalidOperationException(
                    $"'{GetType().FullName}.Compose()' returned a null attribute. Return the attributes " +
                    "this composite declares, or an empty list when it declares none.");
            }
        }

        return [.. composed];
    }
}
