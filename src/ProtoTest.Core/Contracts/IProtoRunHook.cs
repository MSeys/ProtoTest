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
    /// Executed once after every infrastructure piece started and before the first test. The context
    /// reaches the run's applications, so a hook can read what an application serves, such as its API
    /// description. A failure fails the run's start and releases what started.
    /// </summary>
    Task AfterInfrastructureAsync(ProtoRunSetupContext context)
        => Task.CompletedTask;

    /// <summary>
    /// Executed once asynchronously after all tests in the suite have completed.
    /// </summary>
    Task AfterRunAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
