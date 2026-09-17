namespace ProtoTest.Web.Internal;

using ProtoTest.Core;

/// <summary>
/// Finalizes every web session opened during a test. Core client initialization is <see cref="int.MinValue"/>;
/// web finalization runs after normal teardown hooks but before attachment publication and client disposal.
/// </summary>
internal sealed class WebLifecycleHook : IProtoTestHook
{
    public int Order => int.MinValue + 1;

    public Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    public async Task AfterTestAsync(ProtoExecutionContext context)
    {
        foreach (var session in context.Service<WebSessionRegistry>().Sessions.Reverse())
        {
            await session.CompleteAsync();
        }
    }
}
