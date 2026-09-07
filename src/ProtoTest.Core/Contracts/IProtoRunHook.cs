namespace ProtoTest.Core;

/// <summary>
/// Defines lifecycle hooks that run once per test suite execution (before all tests start and after all tests finish).
/// </summary>
public interface IProtoRunHook
{
    /// <summary>
    /// Execution order for the hook. Lower numbers run earlier during setup and later during teardown.
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Executed once asynchronously prior to running any tests in the suite.
    /// </summary>
    Task BeforeRunAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Executed once asynchronously after all tests in the suite have completed.
    /// </summary>
    Task AfterRunAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}