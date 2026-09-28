namespace ProtoTest.Core.Internal;

/// <summary>
/// Completes the registered clients that finish work before attachments are published. Runs after normal
/// teardown hooks and before client disposal and sink publication, so a client's final work is part of
/// the published evidence.
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

