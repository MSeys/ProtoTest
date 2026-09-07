namespace ProtoTest.Core;

/// <summary>
/// Global lifecycle hook responsible for executing all registered <see cref="IProtoClientInitializer"/> instances
/// prior to test execution.
/// </summary>
internal sealed class ProtoClientInitializerHook(IEnumerable<IProtoClientInitializer> initializers) : IProtoTestHook
{
    /// <summary>
    /// Set to <see cref="int.MinValue"/> to ensure clients are initialized before all other hooks.
    /// </summary>
    public int Order => int.MinValue;

    public async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        foreach (var initializer in initializers)
        {
            await initializer.InitializeAsync(context);
        }
    }

    public Task AfterTestAsync(ProtoExecutionContext context)
        => Task.CompletedTask;
}