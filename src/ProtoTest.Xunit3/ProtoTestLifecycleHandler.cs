namespace ProtoTest.Xunit3;

using System.Reflection;
using ProtoTest.Core;
using Xunit.v3;

/// <summary>
/// Internal utility class providing shared context lifecycle management for xUnit v3 test attributes.
/// </summary>
internal static class ProtoTestLifecycleHandler
{
    /// <summary>
    /// Starts the test context and executes before-test hooks.
    /// </summary>
    public static void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var attributes = GetProtoAttributes(methodUnderTest);
        var testId = ProtoTestIdGenerator.Generate(methodUnderTest);

        ProtoTestAssembly.Host
            .StartTestAsync(methodUnderTest.Name, testId, methodUnderTest, attributes)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Executes after-test hooks and cleans up the active <see cref="ProtoExecutionContext"/>.
    /// </summary>
    public static void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        var attributes = GetProtoAttributes(methodUnderTest);

        ProtoTestAssembly.Host
            .CompleteTestAsync(attributes)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Retrieves all <see cref="ProtoAttribute"/> instances declared on the test method and its declaring class.
    /// </summary>
    private static List<ProtoAttribute> GetProtoAttributes(MethodInfo methodUnderTest)
    {
        var attributes = new List<ProtoAttribute>();

        if (methodUnderTest.DeclaringType != null)
        {
            attributes.AddRange(
                methodUnderTest.DeclaringType
                    .GetCustomAttributes(true)
                    .OfType<ProtoAttribute>()
            );
        }

        attributes.AddRange(
            methodUnderTest
                .GetCustomAttributes(true)
                .OfType<ProtoAttribute>()
        );

        return attributes;
    }
}