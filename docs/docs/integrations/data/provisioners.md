---
sidebar_position: 3
title: Provisioners
description: "A provisioner creates a built object in the system under test, returns what the system gave back, and cleans it up afterwards."
---

# Provisioners

`Build()` gives you an object. `CreateAsync()` gives it to a **provisioner**, which creates it in the system under test and returns what the system gave back.

ProtoTest does not decide *how* data gets created — through your public API, a test-support endpoint, a repository, raw SQL. That is the provisioner's job, and you write it once.

## The contract

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
    IAsyncDisposable? Cleanup = null);
```

Input and result are often different: you build a *request*, and get back the *created resource*. From the sample app:

```csharp
public sealed class NorthstarMemberProvisioner
    : IProtoDataProvisioner<InviteMemberRequest, MembershipResponse>
{
    public async ValueTask<ProtoDataProvisioningResult<MembershipResponse>> CreateAsync(
        InviteMemberRequest value,
        ProtoDataProvisioningContext context,
        CancellationToken cancellationToken)
    {
        using var response = await context.Execution.Rest()
            .Body(value)
            .PostAsync("/api/v1/members", ct: cancellationToken);

        response.Should.HaveHttpStatus(HttpStatusCode.Created);
        var member = response.ReadAsJson<MembershipResponse>()
            ?? throw new InvalidOperationException("The sample app returned no provisioned member.");

        return new ProtoDataProvisioningResult<MembershipResponse>(member, member.Id);
    }
}
```

A provisioner can use every other ProtoTest client through `context.Execution`.

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
    .AddData(data => data.AddDefaults<NorthstarDataDefaults>())
    .AddDataProvisioner<InviteMemberRequest, MembershipResponse, NorthstarMemberProvisioner>();
```

```csharp
AddDataProvisioner<T, TProvisioner>()                   // input and result are the same type
AddDataProvisioner<TInput, TResult, TProvisioner>()     // different types
```

Provisioners are resolved from dependency injection and are scoped, so their constructors can take services. A repeat with the same implementation type is a no-op; two *different* provisioners for one input/result pair both register and fail later, when the pair is used. At provisioning time exactly one provisioner must resolve for the pair — none, or more than one, throws `ProtoDataException`; so does a provisioner that returns `null` or a null `Value`.

## Using it

```csharp
// input and result are the same type
var invoice = await Proto.Context.Data().For<Invoice>().CreateAsync();

// input and result differ: name the result
var member = await Proto.Context.Data()
    .For<InviteMemberRequest>()
    .With(request => request.Role, "member")
    .CreateAsync<MembershipResponse>();

// independently resolved objects, one provisioner call each
var members = await Proto.Context.Data()
    .For<InviteMemberRequest>()
    .CreateManyAsync<MembershipResponse>(7);
```

`CreateAsync` builds first and then provisions, so every builder rule still applies. `CreateManyAsync` chooses a fresh object sequence per item and passes each item's builder to its `configure` callback.

## Refs and the identity map

Each successful provision is appended to the test's identity map with the identity the provisioner supplied. `Ref<T>` resolves it again:

```csharp
var projects = await Proto.Context.Data()
    .For<CreateProjectRequest>()
    .CreateManyAsync<ProjectResponse>(2);

var chosen = Proto.Context.Data().Ref<ProjectResponse>(projects[1].Id);
```

- Matching is `entry.Value is T` and, when an identity is given, `StringComparison.Ordinal` equality. Identities are case-sensitive.
- Zero matches and more than one match throw `ProtoDataException` with guidance; when several values of a type exist, an identity is required.
- Only `CreateAsync` and `CreateManyAsync` results are tracked. `Build()` and `BuildMany()` values are never in the map.
- `IProtoData` is scoped to one test, so the map cannot reach data provisioned by another test. Defaults get the same lookup through `ProtoDataValueContext.Ref<T>(identity)`.

A sample where two same-typed values are provisioned and then referenced again is [`tests/ProtoTest.Data.Tests/ProtoDataTests.cs`](https://github.com/MSeys/ProtoTest/blob/main/tests/ProtoTest.Data.Tests/ProtoDataTests.cs).

## Cleaning up

Return a `Cleanup` and ProtoTest disposes it when the test ends:

```csharp
return new ProtoDataProvisioningResult<Invoice>(
    created,
    Identity: created.Id.ToString(),
    Cleanup: new DeleteOnDispose(() => repository.DeleteAsync(created.Id)));
```

```csharp
sealed class DeleteOnDispose(Func<Task> delete) : IAsyncDisposable
{
    public async ValueTask DisposeAsync() => await delete();
}
```

- Cleanups run in **reverse creation order**, so dependent records go before the things they depend on.
- Each cleanup is a test resource (`data:{TypeName}:{sequence}`, kind `data`) released in teardown before the test's clients are disposed.
- Each release is a `data.cleanup` operation carrying `data.type`, `data.identity` and `data.provisioner`.
- If several cleanups fail, they are all attempted and the failures are aggregated as an `AggregateException`.

When cleanup happens at a coarser level — say, the whole tenant is deleted by an [attribute](../../foundation/attributes.md) — just leave `Cleanup` null, as the sample provisioner does.

## Tracing

`data.provision` runs as a child of the `data.create` / `data.create_many` operation and carries `data.input_type`, `data.result_type`, `data.provisioner`, `data.identity`, `data.owned` and `data.value_id`. Each tracked value is also recorded as a `value` item with id `{type}:{identity}` — the user-facing form of `data.value_id` is `value:{type}:{id}`, for example `value:membership:42`. The type segment is snake-cased and has generic arity dropped (`Envelope<InvoiceLine>` becomes `envelope`); without an identity it ends in `#{n}`.

## Limits

- **No retry or transaction semantics.** A provisioner is called once per object; if it fails, the failure is the test's failure.
- **`Identity` is what you say it is.** ProtoTest records the string but cannot check that it names the created record.
- **Cleanup is optional and coarse.** Nothing tracks what a provisioner created unless it returns a `Cleanup`; a cleanup that fails is aggregated, not retried.
- **One provisioner per input/result pair.** Different routes for the same pair are an error at provisioning time, not a selection.

## Links

- [Data overview](./index.md) — install, registration and the builder surface.
- [Defaults](./defaults.md) — what happens before a provisioner runs.
- [Cleanup and resources](../../foundation/lifecycle.md) — how test resources are released.
- The demo's registration and samples: [`samples/ProtoTest.Demo/Setup.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs) and [`samples/ProtoTest.SampleApp.Testing/NorthstarData.cs`](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.SampleApp.Testing/NorthstarData.cs).
