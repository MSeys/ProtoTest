namespace ProtoTest.Data;

/// <summary>Places constructed test data into an application through a project-defined route.</summary>
public interface IProtoDataProvisioner<TInput, TResult>
{
    ValueTask<ProtoDataProvisioningResult<TResult>> CreateAsync(
        TInput value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken);
}

/// <summary>Convenience contract when provisioning input and result use the same type.</summary>
public interface IProtoDataProvisioner<T> : IProtoDataProvisioner<T, T>;

/// <summary>The application value returned after provisioning, plus optional owned cleanup.</summary>
public sealed record ProtoDataProvisioningResult<T>(
    T Value,
    string? Identity = null,
    IAsyncDisposable? Cleanup = null);
