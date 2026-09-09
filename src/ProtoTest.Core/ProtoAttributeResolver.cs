namespace ProtoTest.Core;

using System.Reflection;

/// <summary>
/// Resolves the ProtoTest lifecycle attributes that apply to a test method.
/// </summary>
public static class ProtoAttributeResolver
{
    /// <summary>
    /// Returns inherited class-level attributes followed by inherited method-level attributes.
    /// Lifecycle ordering is subsequently determined by <see cref="ProtoAttribute.Order"/>.
    /// </summary>
    public static IReadOnlyList<ProtoAttribute> Resolve(MethodInfo testMethod)
    {
        ArgumentNullException.ThrowIfNull(testMethod);

        var attributes = new List<ProtoAttribute>();

        if (testMethod.DeclaringType is not null)
        {
            attributes.AddRange(
                testMethod.DeclaringType.GetCustomAttributes<ProtoAttribute>(inherit: true));
        }

        attributes.AddRange(testMethod.GetCustomAttributes<ProtoAttribute>(inherit: true));
        return attributes;
    }
}
