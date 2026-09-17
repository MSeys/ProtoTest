namespace ProtoTest.Data;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

internal sealed class ProtoDataService : IProtoData, IAsyncDisposable
{
    private readonly ProtoDataRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly List<OwnedResource> _ownedResources = [];
    private long _objectSequence;
    private int _disposeStarted;

    public ProtoDataService(ProtoDataRegistry registry, IServiceProvider services)
    {
        _registry = registry;
        _services = services;
    }

    public ProtoDataObjectBuilder<T> For<T>()
        => new(_registry, this, Interlocked.Increment(ref _objectSequence));

    internal async ValueTask<ProtoDataProvisioningResult<TResult>> ProvisionAsync<TInput, TResult>(
        TInput value,
        ProtoExecutionContext execution,
        string parentId,
        CancellationToken cancellationToken)
    {
        var provisioners = _services.GetServices<IProtoDataProvisioner<TInput, TResult>>().ToArray();
        if (provisioners.Length == 0)
        {
            throw new ProtoDataException(
                $"No data provisioner is registered from '{typeof(TInput).FullName}' to '{typeof(TResult).FullName}'.");
        }

        if (provisioners.Length > 1)
        {
            throw new ProtoDataException(
                $"Multiple data provisioners are registered from '{typeof(TInput).FullName}' to '{typeof(TResult).FullName}': " +
                string.Join(", ", provisioners.Select(item => item.GetType().FullName)) + ".");
        }

        var provisioner = provisioners[0];
        using var operation = execution.Trace
            .Operation("data.provision", $"Provision · {typeof(TInput).Name} → {typeof(TResult).Name}", "ProtoTest.Data")
            .With("data.input_type", typeof(TInput).FullName)
            .With("data.result_type", typeof(TResult).FullName)
            .With("data.provisioner", provisioner.GetType().FullName)
            .Parent(parentId)
            .Begin();

        try
        {
            var result = await provisioner.CreateAsync(
                value,
                new ProtoDataProvisioningContext(execution),
                cancellationToken);
            if (result is null || result.Value is null)
            {
                throw new ProtoDataException(
                    $"Data provisioner '{provisioner.GetType().FullName}' returned no '{typeof(TResult).FullName}' value.");
            }

            operation.SetAttribute("data.identity", result.Identity);
            operation.SetAttribute("data.owned", (result.Ownership is not null).ToString().ToLowerInvariant());
            if (result.Ownership is not null)
            {
                _ownedResources.Add(new OwnedResource(
                    typeof(TResult), result.Identity, provisioner.GetType(), result.Ownership, execution.Trace));
            }

            operation.Succeed();
            return result;
        }
        catch (OperationCanceledException exception)
        {
            operation.Cancel(exception);
            throw;
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0) return;

        List<Exception>? failures = null;
        for (var index = _ownedResources.Count - 1; index >= 0; index--)
        {
            var resource = _ownedResources[index];
            using var operation = resource.Trace
                .Operation("data.cleanup", $"Cleanup · {resource.DataType.Name}", "ProtoTest.Data")
                .During(ProtoTracePhase.Teardown)
                .With("data.type", resource.DataType.FullName)
                .With("data.identity", resource.Identity)
                .With("data.provisioner", resource.ProvisionerType.FullName)
                .Begin();
            try
            {
                await resource.Ownership.DisposeAsync();
                operation.Succeed();
            }
            catch (Exception exception)
            {
                operation.Fail(exception);
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more provisioned data resources failed cleanup.", failures);
        }
    }

    private sealed record OwnedResource(
        Type DataType,
        string? Identity,
        Type ProvisionerType,
        IAsyncDisposable Ownership,
        IProtoTraceWriter Trace);
}
