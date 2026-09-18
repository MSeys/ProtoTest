namespace ProtoTest.Core.Internal;

/// <summary>
/// Records what the run owns, then releases it. Runs after the gates and the report sinks - which may
/// still need the resource - and before the trace archive, so both the ownership and the release end up
/// in the run's trace.
/// </summary>
internal sealed class ProtoRunResourceHook(
    ProtoRunResourceStore resources,
    ProtoTraceSession trace) : IProtoRunHook
{
    // AfterRun executes in descending order: gates, reports, run resources, trace archive.
    public int Order => int.MinValue + 1;

    public Task BeforeRunAsync(CancellationToken cancellationToken = default)
    {
        foreach (var resource in resources.Resources)
        {
            trace.RunWriter.SetEntityState(
                resource.Kind,
                resource.Id,
                $"Resource · {resource.Id}",
                new Dictionary<string, string?>
                {
                    ["resource.id"] = resource.Id,
                    ["resource.kind"] = resource.Kind,
                    ["resource.scope"] = "run",
                    ["resource.description"] = resource.Description,
                    ["resource.state"] = "registered"
                },
                change: "owned");
            trace.RunWriter.WriteEvent(
                "resource.owned",
                $"Owned · {resource.Id}",
                "ProtoTest.Core",
                ProtoTracePhase.Run,
                ProtoTraceOutcome.Succeeded,
                new Dictionary<string, string?>
                {
                    ["resource.id"] = resource.Id,
                    ["resource.kind"] = resource.Kind,
                    ["resource.description"] = resource.Description,
                    ["resource.scope"] = ProtoResourceScope.Run.ToString()
                },
                entityKind: resource.Kind,
                entityId: resource.Id);
        }

        return Task.CompletedTask;
    }

    public async Task AfterRunAsync(CancellationToken cancellationToken = default)
    {
        if (!resources.HasResources)
        {
            return;
        }

        var failures = await resources.ReleaseAllAsync(trace.RunWriter);
        LifecycleExceptionHelper.ThrowIfAny(
            "One or more run-scoped resources failed to release.", [.. failures]);
    }
}
