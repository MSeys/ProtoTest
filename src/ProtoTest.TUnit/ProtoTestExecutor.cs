namespace ProtoTest.TUnit;

using System.Reflection;
using global::TUnit.Core.Extensions;
using global::TUnit.Core.Interfaces;
using ProtoTest.Core;

/// <summary>
/// Intercepts test execution in TUnit to manage the <see cref="ProtoExecutionContext"/> and execute registered lifecycle hooks.
/// </summary>
public class ProtoTestExecutor : ITestExecutor
{
    /// <summary>
    /// Executes the test action within an isolated <see cref="ProtoExecutionContext"/>.
    /// </summary>
    /// <param name="context">The execution context provided by TUnit.</param>
    /// <param name="action">The delegate representing the test method execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the execution flow.</returns>
    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        var methodInfo = context.Metadata.TestDetails.MethodMetadata.GetReflectionInfo();
        var attributes = GetProtoAttributes(methodInfo);
        var testId = ProtoTestIdGenerator.Generate(methodInfo);

        try
        {
            await ProtoTestAssembly.Host.StartTestAsync(methodInfo.Name, testId, methodInfo, attributes);
            await action();
        }
        finally
        {
            await ProtoTestAssembly.Host.CompleteTestAsync(attributes);
        }
    }

    /// <summary>
    /// Retrieves all <see cref="ProtoAttribute"/> instances declared on the test method and its declaring class.
    /// </summary>
    /// <param name="methodInfo">Reflection metadata for the test method.</param>
    /// <returns>A list of discovered <see cref="ProtoAttribute"/> instances.</returns>
    private static List<ProtoAttribute> GetProtoAttributes(MethodInfo methodInfo)
    {
        var attributes = new List<ProtoAttribute>();

        if (methodInfo.DeclaringType != null)
        {
            attributes.AddRange(
                methodInfo.DeclaringType
                    .GetCustomAttributes(true)
                    .OfType<ProtoAttribute>()
            );
        }

        attributes.AddRange(
            methodInfo
                .GetCustomAttributes(true)
                .OfType<ProtoAttribute>()
        );

        return attributes;
    }
}