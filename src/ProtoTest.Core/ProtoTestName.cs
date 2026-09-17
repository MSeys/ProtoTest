namespace ProtoTest.Core;

using System.Reflection;

/// <summary>
/// Builds the stable, fully qualified test name recorded for a test method, shared by the adapters
/// so traces and reports use the same naming across frameworks.
/// </summary>
public static class ProtoTestName
{
    /// <summary>Returns "DeclaringType.FullName.MethodName", falling back to the method name alone.</summary>
    public static string FromMethod(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.DeclaringType is null ? method.Name : $"{method.DeclaringType.FullName}.{method.Name}";
    }
}
