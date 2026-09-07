namespace ProtoTest.Core;

using System.Reflection;
using System.Text;

/// <summary>
/// Generates process-scoped 5-digit numeric test IDs with an assembly-derived prefix.
/// </summary>
public static class ProtoTestIdGenerator
{
    private static long _testCounter;

    public static string Generate(MethodInfo method)
    {
        ArgumentNullException.ThrowIfNull(method);

        var assemblyName = method.DeclaringType?.Assembly.GetName().Name ?? "DefaultAssembly";
        var assemblyPrefix = GetAssemblyPrefix(assemblyName);

        // Increments safely across threads and wraps within the three-digit suffix.
        long testIndex = Interlocked.Increment(ref _testCounter) % 1000;

        var finalTestId = (assemblyPrefix * 1000) + (int)testIndex;
        return finalTestId.ToString("D5");
    }

    private static int GetAssemblyPrefix(string assemblyName)
    {
        // 64-bit FNV-1a hash projected to [10, 99]
        ulong hash = 14695981039346656037;
        foreach (byte b in Encoding.UTF8.GetBytes(assemblyName))
        {
            hash ^= b;
            hash *= 1099511628211;
        }

        return (int)(10 + (hash % 90));
    }
}