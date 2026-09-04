namespace ProtoTest.TUnit;

using System.Reflection;
using global::TUnit.Core.Extensions;
using global::TUnit.Core.Interfaces;
using ProtoTest.Core;

/// <summary>
/// TUnit test executor that intercepts test execution to manage the <see cref="ProtoTest"/> lifecycle.
/// Automatically sets up execution context and invokes registered lifecycle hooks.
/// </summary>
public class ProtoTestExecutor : ITestExecutor
{
    /// <summary>
    /// Intercepts the test execution pipeline to manage ProtoTest context and hooks around the test action.
    /// </summary>
    /// <param name="context">The TUnit execution context for the current test.</param>
    /// <param name="action">The delegate representing the actual test method execution.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    public async ValueTask ExecuteTest(TestContext context, Func<ValueTask> action)
    {
        var methodInfo = context.Metadata.TestDetails.MethodMetadata.GetReflectionInfo();
        var attributes = GetProtoAttributes(methodInfo);
        var testId = ProtoTestIdGenerator.Generate(methodInfo);

        // 1. Initialize the ProtoTest context for the current async execution flow
        ProtoTestAssembly.Host.BeginTestContext(methodInfo.Name, testId);

        try
        {
            // 2. Execute all registered ProtoTest before-hooks
            await ProtoTestAssembly.Host.ExecuteBeforeHooksAsync(attributes);

            // 3. Execute the actual test method within the active ProtoTest context
            await action();

            // 4. Execute all registered ProtoTest after-hooks
            await ProtoTestAssembly.Host.ExecuteAfterHooksAsync(attributes);
        }
        finally
        {
            // 5. Clean up the test context upon completion
            ProtoTestAssembly.Host.EndTestContext();
        }
    }

    /// <summary>
    /// Collects all <see cref="ProtoAttribute"/> instances declared on the test method and its target class.
    /// </summary>
    /// <param name="methodInfo">Reflection metadata for the test method.</param>
    /// <returns>A list of <see cref="ProtoAttribute"/> instances discovered in the hierarchy.</returns>
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