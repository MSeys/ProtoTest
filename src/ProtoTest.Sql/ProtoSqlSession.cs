namespace ProtoTest.Sql;

using System.Data.Common;
using System.Runtime.ExceptionServices;
using ProtoTest.Core;

/// <summary>
/// The per-test database session: the connection ProtoTest owns, the transaction every access
/// technology must enlist in when the isolation strategy needs one, and the lifecycle that opens,
/// starts and releases both. One type owns the transaction, so no hook can leave one half-started.
/// </summary>
public sealed class ProtoSqlSession
{
    /// <summary>The trace source every SQL event declares.</summary>
    internal const string TraceSource = "ProtoTest.Sql";

    private readonly SqlOptions _options;

    internal ProtoSqlSession(DbConnection connection, SqlOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        Connection = connection;
        _options = options;
    }

    /// <summary>Gets the connection owned by this test.</summary>
    public DbConnection Connection { get; }

    /// <summary>Gets the test's transaction, or <see langword="null"/> when isolation is <see cref="SqlIsolation.None"/>.</summary>
    public DbTransaction? Transaction { get; private set; }

    /// <summary>Opens the connection and, when the isolation strategy needs one, starts the test's transaction.</summary>
    internal async Task StartAsync(ProtoExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var open = context.Trace
            .Operation("sql.connection.open", $"SQL · open {Connection.GetType().Name}", TraceSource)
            .During(ProtoTracePhase.Setup)
            .With("sql.connection.type", Connection.GetType().FullName)
            .Begin();
        try
        {
            await Connection.OpenAsync();
            if (_options.Isolation == SqlIsolation.Transaction)
            {
                await context.Trace
                    .Operation("sql.transaction.begin", "SQL · begin transaction", TraceSource)
                    .During(ProtoTracePhase.Setup)
                    .With("sql.isolation", _options.Isolation.ToString())
                    .RunAsync(async () => Transaction = await Connection.BeginTransactionAsync());
            }

            open.Succeed();
        }
        catch (Exception exception)
        {
            open.Fail(exception);
            throw;
        }
    }

    /// <summary>
    /// Releases the test's transaction and connection. The transaction rolls back first and both
    /// dispose afterwards, each as its own step, so a failing rollback never skips the disposals.
    /// </summary>
    internal async ValueTask ReleaseAsync(ProtoResourceReleaseContext release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var transaction = Transaction;
        Transaction = null;
        var flow = new ProtoFlow("sql.release", TraceSource, ProtoFlowFailureMode.Collect);
        if (transaction is not null)
        {
            flow.Step("rollback", async ct => await release.Trace
                .Operation("sql.transaction.rollback", "SQL · rollback transaction", TraceSource)
                .During(release.Phase)
                .With("sql.isolation", _options.Isolation.ToString())
                .RunAsync(async () => await transaction.RollbackAsync(ct)));
            flow.Step("dispose transaction", _ => transaction.DisposeAsync());
        }

        flow.Step("dispose connection", _ => Connection.DisposeAsync());
        var result = await flow.RunAsync(release.Trace, release.CancellationToken);
        if (result.Succeeded)
        {
            return;
        }

        if (result.Failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(result.Failures[0]).Throw();
        }

        throw new AggregateException(result.Failures);
    }
}
