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
        if (methodInfo is null)
        {
            // A source-generated test exposes no reflection method to prepare from; running the body
            // unwrapped keeps it executing instead of failing on the executor's own assumption.
            await action();
            return;
        }

        var preparation = ProtoTestAdapter.Prepare(methodInfo, ProtoTestAssembly.Host);
        if (!preparation.CanRun)
        {
            global::TUnit.Core.Skip.Test(preparation.SkipReason!);
        }

        var scope = await ProtoTestScope.StartAsync(
            preparation, ProtoTestAssembly.Host, new TUnitAttachmentPublisher(context));
        var result = ProtoTestResult.Unknown;
        Exception? failure = null;
        try
        {
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
        finally
        {
            scope.Result = result;
            await scope.DisposeAsync();
        }

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

}
