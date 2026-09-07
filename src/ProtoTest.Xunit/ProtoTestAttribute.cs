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
        var attributes = GetProtoAttributes(methodUnderTest);
        var testId = ProtoTestIdGenerator.Generate(methodUnderTest);

        ProtoTestAssembly.Host
            .StartTestAsync(methodUnderTest.Name, testId, methodUnderTest, attributes)
            .GetAwaiter()
            .GetResult();
    }

    /// <inheritdoc />
    public override void After(MethodInfo methodUnderTest)
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
    /// <param name="methodUnderTest">Reflection metadata for the test method.</param>
    /// <returns>A list of discovered <see cref="ProtoAttribute"/> instances.</returns>
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