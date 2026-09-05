namespace ProtoTest.Core;

using System.Reflection;
using System.Text;

/// <summary>
/// Generates deterministic 5-digit numeric test IDs scoped across assembly boundaries.
/// </summary>
public static class ProtoTestIdGenerator
{
    private static long _testCounter;

    public static string Generate(MethodInfo method)
    {
        var assemblyName = method.DeclaringType?.Assembly.GetName().Name ?? "DefaultAssembly";
        int assemblyPrefix = GetAssemblyPrefix(assemblyName);

        // Increments safely across threads, wraps within 0-999
        long testIndex = Interlocked.Increment(ref _testCounter) % 1000;

        int finalTestId = (assemblyPrefix * 1000) + (int)testIndex;
        return finalTestId.ToString();
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