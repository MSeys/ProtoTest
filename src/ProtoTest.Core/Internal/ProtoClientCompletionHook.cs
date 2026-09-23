namespace ProtoTest.Core.Internal;

/// <summary>
/// Completes the registered clients that finish work before attachments are published. Runs at the same
/// point the web lifecycle used to occupy: after normal teardown hooks, before client disposal and sink
/// publication.
/// </summary>
internal sealed class ProtoClientCompletionHook : IProtoTestHook
{
    public int Order => ProtoHookOrder.ClientCompletion;

    public Task BeforeTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    public async Task AfterTestAsync(ProtoExecutionContext context)
    {
        List<Exception>? exceptions = null;
        foreach (var client in context.RegisteredClients.Reverse())
        {
            if (client is not IProtoClientCompletion completion)
            {
                continue;
            }

            try
            {
                await completion.CompleteAsync();
            }
            catch (Exception exception)
            {
                (exceptions ??= []).Add(exception);
            }
        }

        if (exceptions is not null)
        {
            LifecycleExceptionHelper.ThrowIfAny("One or more clients failed to complete.", exceptions);
        }
    }
}

