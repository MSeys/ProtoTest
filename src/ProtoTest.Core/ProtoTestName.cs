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

    /// <summary>
    /// Composes one parameterized row's trace name from the method's stable name and the row's
    /// arguments - "DeclaringType.FullName.MethodName[1, admin]" - so rows of one method stay
    /// distinguishable in traces and reports. MSTest and TUnit share this form; NUnit and xUnit
    /// record their own runner's display name instead.
    /// </summary>
    public static string ForRow(MethodInfo method, IEnumerable<object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(arguments);
        return $"{FromMethod(method)}[{string.Join(", ", arguments.Select(argument => argument?.ToString() ?? "null"))}]";
    }
}
