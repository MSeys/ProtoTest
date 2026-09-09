namespace ProtoTest.TUnit;

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
        var attributes = ProtoAttributeResolver.Resolve(methodInfo);
        try
        {
            await ProtoTestAssembly.Host.StartTestAsync(methodInfo.Name, methodInfo, attributes);
            await action();
        }
        finally
        {
            await ProtoTestAssembly.Host.CompleteTestAsync();
        }
    }

}
