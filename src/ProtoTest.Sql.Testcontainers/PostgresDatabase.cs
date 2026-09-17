namespace ProtoTest.Sql.Testcontainers;

using ProtoTest.Core;

// global:: because this assembly's own namespace ends in Testcontainers.
using global::Testcontainers.PostgreSql;

/// <summary>
/// A PostgreSQL container owned by the whole run: started once for the suite and released when the
/// host is disposed, after the run has stopped and the reports are written. Register it with
/// <c>AddResource</c> and hand its connection string to the application and to the tests so both work
/// against the same database.
/// </summary>
public sealed class PostgresDatabase : IProtoResource
{
    private readonly PostgreSqlContainer _container;
    private int _released;

    private PostgresDatabase(PostgreSqlContainer container, string connectionString)
    {
        _container = container;
        ConnectionString = connectionString;
    }

    public string Id => "database:postgres";

    public string Kind => "database";

    public string Description => "PostgreSQL container";

    public ProtoResourceScope Scope => ProtoResourceScope.Run;

    /// <summary>Gets the connection string of the started container.</summary>
    public string ConnectionString { get; }

    /// <summary>Starts a container, or throws with the reason it could not start.</summary>
    public static PostgresDatabase Start(Action<PostgreSqlBuilder>? configure = null)
        => TryStart(configure, out var database, out var error)
            ? database!
            : throw new InvalidOperationException($"The PostgreSQL container did not start: {error}");

    /// <summary>
    /// Starts a container, reporting why it could not start instead of throwing - a machine without a
    /// container runtime should be able to fall back or skip rather than fail the run.
    /// </summary>
    public static bool TryStart(
        Action<PostgreSqlBuilder>? configure,
        out PostgresDatabase? database,
        out string? error)
    {
        var builder = new PostgreSqlBuilder("postgres:16-alpine");
        configure?.Invoke(builder);

        PostgreSqlContainer? container = null;
        try
        {
            container = builder.Build();

            // Setup is synchronous: an assembly fixture configures the host before any test runs.
            container.StartAsync().GetAwaiter().GetResult();
            database = new PostgresDatabase(container, container.GetConnectionString());
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            database = null;
            error = $"{exception.GetType().Name}: {exception.Message}";
            if (container is not null)
            {
                try
                {
                    container.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception)
                {
                    // The original start failure is what the caller needs to see.
                }
            }

            return false;
        }
    }

    public ValueTask ReleaseAsync(ProtoResourceReleaseContext context) => DisposeAsync();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
        {
            return;
        }

        await _container.DisposeAsync();
    }
}
