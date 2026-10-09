namespace ProtoTest.TUnit;

using System.Reflection;
using System.Runtime.ExceptionServices;
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

        var preparation = ProtoTestAdapter.Prepare(methodInfo, ProtoTestAssembly.Host, RowName(methodInfo, context));
        if (!preparation.CanRun)
        {
            global::TUnit.Core.Skip.Test(preparation.SkipReason!);
        }

        var scope = await ProtoTestScope.StartAsync(
            preparation, ProtoTestAssembly.Host, new TUnitAttachmentPublisher(context),
            context.Execution.CancellationToken);
        var result = ProtoTestResult.Unknown;
        Exception? failure = null;
        ProtoCleanupException? cleanup = null;
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
        catch (Exception exception)
        {
            // The shared classifier decides cancelled vs failed, so TUnit agrees with the other
            // adapters on a body that cancels.
            result = ProtoTestResult.FromException(exception);
            failure = exception;
        }
        finally
        {
            scope.Result = result;
            try
            {
                await scope.DisposeAsync();
            }
            catch (ProtoCleanupException exception)
            {
                // Captured here so a dispose failure cannot replace the body exception. When the body
                // failed, this exception is what TUnit sees: its message leads with that failure.
                cleanup = exception;
            }
        }

        if (cleanup is not null)
        {
            ExceptionDispatchInfo.Capture(cleanup).Throw();
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// The row's trace name: TUnit's display name is the method name alone, so a parameterized test
    /// gets the shared row form - the stable method name with the row's arguments appended, the same
    /// shape MSTest records. A plain test keeps the stable fully qualified name.
    /// </summary>
    private static string? RowName(MethodInfo methodInfo, TestContext context)
    {
        var arguments = context.Metadata.TestDetails.TestMethodArguments;
        return arguments is { Length: > 0 }
            ? ProtoTestName.ForRow(methodInfo, arguments)
            : null;
    }
}
