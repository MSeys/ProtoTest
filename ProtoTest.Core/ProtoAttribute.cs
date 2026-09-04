namespace ProtoTest.Core;

/// <summary>
/// Base attribute for defining test-level or class-level lifecycle execution hooks.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public abstract class ProtoAttribute : Attribute
{
    /// <summary>
    /// Order of execution for the attribute. Lower values are executed earlier in the lifecycle.
    /// </summary>
    public int Order { get; init; } = 0;

    /// <summary>
    /// Executed asynchronously before the decorated test or class tests execute.
    /// </summary>
    public virtual Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    /// <summary>
    /// Executed asynchronously after the decorated test or class tests complete.
    /// </summary>
    public virtual Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}