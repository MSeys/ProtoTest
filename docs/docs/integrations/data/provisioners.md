---
sidebar_position: 3
title: Provisioners
---

# Provisioners

`Build()` gives you an object. `CreateAsync()` gives it to a **provisioner**, which creates it in the system under test and returns what the system gave back.

ProtoTest doesn't decide *how* data gets created — through your public API, a test-support endpoint, a repository, raw SQL. That's the provisioner's job, and you write it once.

## Writing one

```csharp
public interface IProtoDataProvisioner<TInput, TResult>
{
    ValueTask<ProtoDataProvisioningResult<TResult>> CreateAsync(
        TInput value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken);
}

public interface IProtoDataProvisioner<T> : IProtoDataProvisioner<T, T>;

public sealed record ProtoDataProvisioningResult<T>(
    T Value,
    string? Identity = null,
    IAsyncDisposable? Ownership = null);
```

Input and result are often different: you build a *request*, and get back the *created resource*. From the sample app:

```csharp
public sealed class SampleUserProvisioner : IProtoDataProvisioner<CreateUserRequest, UserResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<UserResponse>> CreateAsync(
        CreateUserRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        var environment = context.Execution.Context<SampleEnvironmentContext>();

        using var response = await context.Execution.Rest("Api")
            .WithoutAuth()
            .Body(value)
            .PostAsync("/test-support/environments/{tenant}/users", new { environment.Tenant }, cancellationToken);

        response.ShouldHaveStatus(HttpStatusCode.Created);
        var user = response.ReadAsJson<UserResponse>()
            ?? throw new InvalidOperationException("The sample app returned no provisioned user.");

        return new ProtoDataProvisioningResult<UserResponse>(user, user.Id);
    }
}
```

Note that a provisioner can use every other ProtoTest client through `context.Execution`.

`ProtoDataProvisioningContext` has:

| Member | |
| --- | --- |
| `Execution` | the running test's `ProtoExecutionContext` |
| `Services` | the test's service provider |
| `TestId` | the running test's id |
| `Trace` | the trace writer, for adding your own entries |

## Registering

```csharp
builder
    .AddData(data => data.AddDefaults<SampleAppDataDefaults>())
    .AddDataProvisioner<CreateUserRequest, UserResponse, SampleUserProvisioner>();
```

```csharp
AddDataProvisioner<T, TProvisioner>()                   // input and result are the same type
AddDataProvisioner<TInput, TResult, TProvisioner>()     // different types
```

Provisioners are resolved from dependency injection, so their constructors can take services.

## Using it

```csharp
// input and result are the same type
var invoice = await Proto.Context.Data().For<Invoice>().CreateAsync();

// input and result differ: name the result
var user = await Proto.Context.Data()
    .For<CreateUserRequest>()
    .With(request => request.Role, SampleRoles.Member)
    .CreateAsync<UserResponse>();
```

Exactly one provisioner must be registered for each input/result pair. None — or more than one — throws `ProtoDataException`, as does a provisioner that returns `null`.

## Cleaning up

Return an `Ownership` and ProtoTest disposes it when the test ends:

```csharp
return new ProtoDataProvisioningResult<Invoice>(
    created,
    Identity: created.Id.ToString(),
    Ownership: new DeleteOnDispose(() => repository.DeleteAsync(created.Id)));
```

```csharp
sealed class DeleteOnDispose(Func<Task> delete) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await delete();
}
```

- Ownerships are disposed in **reverse creation order**, so dependent records go before the things they depend on.
- Each cleanup is a `data.cleanup` entry in the teardown phase of the trace.
- If several cleanups fail, they're all attempted and the failures are reported together as an `AggregateException`.

When cleanup happens at a coarser level — say, the whole tenant is deleted by an [attribute](../../foundation/attributes.md) — just leave `Ownership` null, as the sample provisioner does.

`Identity` is optional and appears in the trace so you can find the created record in your application's logs.
