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
    public override void Before(MethodInfo methodUnderTest)
    {
        var attributes = GetProtoAttributes(methodUnderTest);
        var testId = ProtoTestIdGenerator.Generate(methodUnderTest);

        // 1. Synchronously bind context on xUnit's test execution frame
        ProtoTestAssembly.Host.BeginTestContext(methodUnderTest.Name, testId);

        // 2. Execute async pre-test hooks & attributes
        ProtoTestAssembly.Host
            .ExecuteBeforeHooksAsync(attributes)
            .GetAwaiter()
            .GetResult();
    }

    public override void After(MethodInfo methodUnderTest)
    {
        var attributes = GetProtoAttributes(methodUnderTest);

        try
        {
            // 1. Execute async post-test hooks & attributes and dispose context scope
            ProtoTestAssembly.Host
                .ExecuteAfterHooksAsync(attributes)
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            // 2. Synchronously unbind context from xUnit's test execution frame
            ProtoTestAssembly.Host.EndTestContext();
        }
    }

    private static List<ProtoAttribute> GetProtoAttributes(MethodInfo methodUnderTest)
    {
        var attributes = new List<ProtoAttribute>();

        // 1. Resolve class-level attributes
        if (methodUnderTest.DeclaringType != null)
        {
            attributes.AddRange(
                methodUnderTest.DeclaringType
                    .GetCustomAttributes(true)
                    .OfType<ProtoAttribute>()
            );
        }

        // 2. Resolve method-level attributes
        attributes.AddRange(
            methodUnderTest
                .GetCustomAttributes(true)
                .OfType<ProtoAttribute>()
        );

        return attributes;
    }
}