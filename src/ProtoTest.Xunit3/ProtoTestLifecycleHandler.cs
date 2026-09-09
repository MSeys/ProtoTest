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
        var attributes = ProtoAttributeResolver.Resolve(methodUnderTest);
        ProtoTestAssembly.Host
            .StartTestAsync(methodUnderTest.Name, methodUnderTest, attributes, Xunit3AttachmentPublisher.Instance)
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>
    /// Executes after-test hooks and cleans up the active <see cref="ProtoExecutionContext"/>.
    /// </summary>
    public static void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        ProtoTestAssembly.Host
            .CompleteTestAsync()
            .GetAwaiter()
            .GetResult();
    }

}
