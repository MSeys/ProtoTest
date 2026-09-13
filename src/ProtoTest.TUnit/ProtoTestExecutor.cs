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
        var lifecycleStarted = false;
        var result = ProtoTestResult.Unknown;
        try
        {
            await ProtoTestAssembly.Host.StartTestAsync(
                methodInfo.Name, methodInfo, attributes, new TUnitAttachmentPublisher(context));
            lifecycleStarted = true;
            await action();
            result = ProtoTestResult.Passed;
        }
        catch (OperationCanceledException exception)
        {
            result = ProtoTestResult.Cancelled(exception);
            throw;
        }
        catch (Exception exception)
        {
            result = ProtoTestResult.Failed(exception);
            throw;
        }
        finally
        {
            if (lifecycleStarted)
            {
                await ProtoTestAssembly.Host.CompleteTestAsync(result);
            }
        }
    }

}
