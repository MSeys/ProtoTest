namespace ProtoTest.Xunit;

using System.Reflection;
using global::Xunit.Sdk;
using ProtoTest.Core;

/// <summary>
/// xUnit v2 test attribute that manages the <see cref="ProtoExecutionContext"/> lifecycle for each test method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class ProtoTestAttribute : BeforeAfterTestAttribute
{
    /// <inheritdoc />
    public override void Before(MethodInfo methodUnderTest)
    {
        var attributes = ProtoAttributeResolver.Resolve(methodUnderTest);
        ProtoTestAssembly.Host
            .StartTestAsync(methodUnderTest.Name, methodUnderTest, attributes, Xunit2AttachmentPublisher.Instance)
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest)
    {
        ProtoTestAssembly.Host
            .CompleteTestAsync()
            .GetAwaiter()
            .GetResult();
    }

}
