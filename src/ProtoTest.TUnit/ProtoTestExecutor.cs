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
        var methodInfo = context.Metadata.TestDetails.MethodMetadata.GetReflectionInfo()
            ?? throw new InvalidOperationException("TUnit did not expose a reflection MethodInfo for the test method.");
        var attributes = ProtoAttributeResolver.Resolve(methodInfo);
        var skipReason = ProtoTestSkip.GetReason(attributes, ProtoTestAssembly.Host);
        if (skipReason is not null)
        {
            // Skipping before the lifecycle starts keeps the trace honest: nothing ran, so nothing failed.
            global::TUnit.Core.Skip.Test(skipReason);
        }

        var lifecycleStarted = false;
        var result = ProtoTestResult.Unknown;
        Exception? failure = null;
        try
        {
            await ProtoTestAssembly.Host.StartTestAsync(
                ProtoTestName.FromMethod(methodInfo), methodInfo, attributes, new TUnitAttachmentPublisher(context));
            lifecycleStarted = true;
            await action();
            result = ProtoTestResult.Passed;
        }
        catch (global::TUnit.Core.Exceptions.SkipTestException exception)
        {
            // A test that skips itself from its body is reported as skipped, not failed.
            result = ProtoTestResult.Skipped;
            failure = exception;
        }
        catch (OperationCanceledException exception)
        {
            result = ProtoTestResult.Cancelled(exception);
            failure = exception;
        }
        catch (Exception exception)
        {
            result = ProtoTestResult.Failed(exception);
            failure = exception;
        }

        if (lifecycleStarted)
        {
            try
            {
                await ProtoTestAssembly.Host.CompleteTestAsync(result);
            }
            catch when (failure is not null)
            {
                // Teardown must never replace the original test failure.
            }
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

}
