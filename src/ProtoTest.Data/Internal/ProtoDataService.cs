namespace ProtoTest.Data.Internal;

using Microsoft.Extensions.DependencyInjection;
using ProtoTest.Core;

internal sealed class ProtoDataService : IProtoData
{
    private readonly ProtoDataRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly object _gate = new();
    private readonly List<ProvisionedObject> _provisioned = [];
    private long _objectSequence;
    private long _valueSequence;
    private int _provisionSequence;

    public ProtoDataService(ProtoDataRegistry registry, IServiceProvider services)
    {
        _registry = registry;
        _services = services;
    }

    public ProtoDataObjectBuilder<T> For<T>()
        => new(_registry, this, Interlocked.Increment(ref _objectSequence));

    public T Ref<T>(string? identity = null)
    {
        ProvisionedObject[] matches;
        lock (_gate)
        {
            matches =
            [
                .. _provisioned.Where(entry => entry.Value is T
                    && (identity is null || string.Equals(entry.Identity, identity, StringComparison.Ordinal)))
            ];
        }

        if (matches.Length == 0)
        {
            throw new ProtoDataException(identity is null
                ? $"No '{typeof(T).Name}' has been provisioned in this test, so it cannot be referenced. {DescribeProvisioned()}"
                : $"No '{typeof(T).Name}' with identity '{identity}' has been provisioned in this test. {DescribeProvisioned()}");
        }

        if (matches.Length > 1)
        {
            throw new ProtoDataException(identity is null
                ? $"Several '{typeof(T).Name}' values have been provisioned in this test. " +
                  $"Pass an identity to Ref<{typeof(T).Name}>(...) to choose one."
                : $"Several '{typeof(T).Name}' values were provisioned with the identity '{identity}', " +
                  $"so it does not choose one. Pass an identity unique to the value you mean to Ref<{typeof(T).Name}>(...).");
        }

        return (T)matches[0].Value;
    }

    private string DescribeProvisioned()
    {
        lock (_gate)
        {
            var types = _provisioned
                .Select(entry => entry.Value.GetType().Name)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return types.Length == 0
                ? "Nothing has been provisioned yet."
                : $"Provisioned so far: {string.Join(", ", types)}.";
        }
    }

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
            operation.SetAttribute("data.owned", (result.Cleanup is not null).ToString().ToLowerInvariant());
            var valueKind = typeof(TResult).Name;
            var valueTypeSegment = ValueTypeSegment(typeof(TResult));
            var valueIdentity = result.Identity is { Length: > 0 } identity
                ? identity
                : $"#{Interlocked.Increment(ref _valueSequence)}";
            operation.SetAttribute("data.value_id", $"value:{valueTypeSegment}:{valueIdentity}");
            var valueName = result.Identity is { Length: > 0 }
                ? $"Value · {valueKind} '{result.Identity}'"
                : $"Value · {valueKind}";
            execution.Trace.Value(
                "value",
                $"{valueTypeSegment}:{valueIdentity}",
                valueName,
                "created",
                new Dictionary<string, string?>
                {
                    ["value.type"] = typeof(TResult).FullName,
                    ["value.identity"] = result.Identity,
                    ["value.provisioner"] = provisioner.GetType().FullName,
                    ["value.owned"] = (result.Cleanup is not null).ToString().ToLowerInvariant()
                },
                scope: execution.TestName);
            lock (_gate)
            {
                _provisioned.Add(new ProvisionedObject(result.Identity, result.Value));
            }

            if (result.Cleanup is not null)
            {
                RegisterOwnedData(execution, result.Cleanup, typeof(TResult), result.Identity, provisioner.GetType());
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

    /// <summary>
    /// The value-id segment for a result type. Generic arity is dropped so a <c>List&lt;T&gt;</c> reads
    /// as <c>list</c> instead of leaking the CLR backtick name into the trace id.
    /// </summary>
    private static string ValueTypeSegment(Type type)
    {
        var name = type.Name;
        var arity = name.IndexOf('`');
        return ProtoTraceItemKeys.TypeSegment(arity >= 0 ? name[..arity] : name);
    }

    private sealed record ProvisionedObject(string? Identity, object Value);

    private void RegisterOwnedData(
        ProtoExecutionContext execution,
        IAsyncDisposable cleanup,
        Type dataType,
        string? identity,
        Type provisionerType)
    {
        var sequence = Interlocked.Increment(ref _provisionSequence);
        execution.RegisterResource(new ProtoResource(
            $"data:{dataType.Name}:{sequence}",
            "data",
            identity is null
                ? $"Provisioned {dataType.Name}"
                : $"Provisioned {dataType.Name} '{identity}'",
            release => CleanupAsync(cleanup, dataType, identity, provisionerType, release)));
    }

    private static async ValueTask CleanupAsync(
        IAsyncDisposable cleanup,
        Type dataType,
        string? identity,
        Type provisionerType,
        ProtoResourceReleaseContext release)
    {
        using var operation = release.Trace
            .Operation("data.cleanup", $"Cleanup · {dataType.Name}", "ProtoTest.Data")
            .During(release.Phase)
            .With("data.type", dataType.FullName)
            .With("data.identity", identity)
            .With("data.provisioner", provisionerType.FullName)
            .Begin();
        try
        {
            await cleanup.DisposeAsync();
            operation.Succeed();
        }
        catch (Exception exception)
        {
            operation.Fail(exception);
            throw;
        }
    }
}
