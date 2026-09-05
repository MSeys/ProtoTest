namespace ProtoTest.Core;
/// <summary>
/// Defines a global lifecycle hook executed before and after each test.
/// </summary>
public interface IProtoHook
{
    /// <summary>
    /// Order of execution for the hook. Lower values are executed earlier in the lifecycle.
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Executed asynchronously before a test starts.
    /// </summary>
    Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    /// <summary>
    /// Executed asynchronously after a test completes.
    /// </summary>
    Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}