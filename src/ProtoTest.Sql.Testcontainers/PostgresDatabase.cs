namespace ProtoTest.Sql.Testcontainers;

using ProtoTest.Testcontainers;

// global:: because this assembly's own namespace ends in Testcontainers.
using global::Testcontainers.PostgreSql;

/// <summary>
/// A PostgreSQL container owned by the whole run: started once for the suite and released when the
/// host is disposed, after the run has stopped and the reports are written. Register it with
/// <c>AddInfrastructure</c> and hand its connection string to the application and to the tests so both
/// work against the same database.
/// </summary>
public sealed class PostgresDatabase : ProtoContainerResource<PostgreSqlContainer>
{
    private PostgresDatabase(Action<PostgreSqlBuilder>? configure)
        : base(
            () =>
            {
                var builder = new PostgreSqlBuilder("postgres:16-alpine");
                configure?.Invoke(builder);
                return builder.Build();
            },
            (container, cancellationToken) => container.StartAsync(cancellationToken),
            container => container.GetConnectionString())
    {
    }

    public override string Id => "database:postgres";

    public override string Kind => "database";

    public override string Description => "PostgreSQL container";

    /// <summary>Creates the resource without starting it; the host starts it with the run.</summary>
    public static PostgresDatabase Container(Action<PostgreSqlBuilder>? configure = null)
        => new(configure);

    /// <summary>Starts a container now, or throws with the reason it could not start.</summary>
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
        var candidate = Container(configure);
        try
        {
            candidate.StartAsync().GetAwaiter().GetResult();
            database = candidate;
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            database = null;
            error = $"{exception.GetType().Name}: {exception.Message}";
            try
            {
                candidate.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch
            {
                // The start failure is what the caller needs to see.
            }

            return false;
        }
    }
}
