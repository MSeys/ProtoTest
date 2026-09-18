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
        List<Exception>? exceptions = null;
        foreach (var session in context.Service<WebSessionRegistry>().Sessions.Reverse())
        {
            try
            {
                await session.CompleteAsync();
            }
            catch (Exception exception)
            {
                (exceptions ??= []).Add(exception);
            }
        }

        if (exceptions is not null)
        {
            throw new AggregateException("One or more web sessions failed to complete.", exceptions);
        }
    }
}
