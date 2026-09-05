namespace ProtoTest.Xunit3;

using ProtoTest.Core;
using System.Reflection;
using Xunit;
using Xunit.v3;

/// <summary>
/// Marks a method as a ProtoTest Fact in xUnit v3 and manages its context lifecycle.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class ProtoTestFactAttribute : FactAttribute, IBeforeAfterTestAttribute
{
    public void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        var attributes = GetProtoAttributes(methodUnderTest);
        var testId = ProtoTestIdGenerator.Generate(methodUnderTest);

        ProtoTestAssembly.Host.BeginTestContext(methodUnderTest.Name, testId, methodUnderTest);

        ProtoTestAssembly.Host
            .ExecuteBeforeHooksAsync(attributes)
            .GetAwaiter()
            .GetResult();
    }

    public void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        var attributes = GetProtoAttributes(methodUnderTest);

        try
        {
            ProtoTestAssembly.Host
                .ExecuteAfterHooksAsync(attributes)
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            ProtoTestAssembly.Host.EndTestContext();
        }
    }

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