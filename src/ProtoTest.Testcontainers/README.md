# ProtoTest.Testcontainers

The `ProtoContainerResource<TContainer>` base for run-scoped containers: start-once, release-once and `TryStart`, with no container-library dependency of its own.

```bash
dotnet add package ProtoTest.Testcontainers
```

Technology packages build on it — `ProtoTest.Sql.Testcontainers` and `ProtoTest.Messaging.RabbitMq.Testcontainers` — each keeping its own Testcontainers module dependency.

## Quick start

```csharp
public sealed class SearchIndexContainer : ProtoContainerResource<PostgreSqlContainer>
{
    public SearchIndexContainer() : base(
        () => new PostgreSqlBuilder("postgres:16-alpine").Build(),
        (container, cancellationToken) => container.StartAsync(cancellationToken),
        container => container.GetConnectionString())
    {
    }

    public override string Id => "search:postgres";
    public override string Kind => "search";
    public override string Description => "PostgreSQL search index";
}

// The host starts it before the run and fills the key for tests and applications alike.
builder.AddInfrastructure(new SearchIndexContainer(), "Search:Postgres:ConnectionString");
```

## What it adds

- **Run-scoped resource** — `Scope` is `ProtoResourceScope.Run`; `ConnectionString` is empty until the container starts and `IsStarted` reports the state.
- **Start semantics** — concurrent callers await one cached start task, a failed start stays retryable, and `StartAsync` after release throws `ObjectDisposedException`.
- **Fixture fallback** — `TryStartContainer` starts a candidate synchronously and returns `"{ExceptionType}: {message}"` instead of throwing, so a fixture can fall back or skip.
- **Registration contract** — `AddInfrastructure` owns and starts the resource; `AddResource` alone only registers it for release and starts nothing.

A container runtime is required at host start and there is no built-in skip; a missing runtime fails the run before any test-level skip condition is evaluated.

## Learn more

- [Infrastructure](https://prototest.dev/docs/foundation/infrastructure)
- [Demo container registration](https://github.com/MSeys/ProtoTest/blob/main/samples/ProtoTest.Demo/Setup.cs)
