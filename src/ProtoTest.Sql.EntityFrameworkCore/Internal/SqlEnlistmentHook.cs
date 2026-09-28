namespace ProtoTest.Sql.EntityFrameworkCore.Internal;

using Microsoft.EntityFrameworkCore;
using ProtoTest.Core;
using ProtoTest.Sql;

/// <summary>
/// Enlists the test's <see cref="DbContext"/> in the transaction ProtoTest.Sql opened. Without this the
/// provider refuses to execute, because ADO.NET requires a command to carry the transaction of a
/// connection that has a pending local transaction. When the SQL address keys are declared and none is
/// provided, the integration is inert: the hook leaves the session and the context untouched, so no
/// connection factory or context options run during setup, and the accessors name the missing keys.
/// </summary>
internal sealed class SqlEnlistmentHook<TContext> : IProtoTestHook
    where TContext : DbContext
{
    private const string TraceSource = "ProtoTest.Sql.EntityFrameworkCore";

    // Runs after the connection hook, which opens the connection and starts the transaction.
    public int Order => SqlHookOrders.Enlistment;

    public async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        // Check the address rule before resolving the session or the context: resolving either would
        // run the connection factory during an inert setup, which is the failure the rule exists to
        // prevent. A test that needs the context is gated by the store capability and skips instead.
        if (context.TryService<SqlOptions>() is { } options && SqlAddressRule.IsInert(context, options))
        {
            return;
        }

        var session = context.Service<ProtoSqlSession>();
        var dbContext = context.Service<TContext>();

        // The connection is validated even without a transaction: an AddEntityFrameworkCore call after
        // a host AddDbContext preserves the host's configuration, and a host AddDbContext after
        // AddEntityFrameworkCore is dropped by Entity Framework Core. Either way a context that does
        // not use the test's connection is a configuration error the test should hear about.
        if (!ReferenceEquals(dbContext.Database.GetDbConnection(), session.Connection))
        {
            throw new InvalidOperationException(
                $"The DbContext '{typeof(TContext).Name}' uses its own connection, so it cannot use the " +
                "database ProtoTest.Sql owns. Configure it with the test connection, for example " +
                "UseNpgsql(services.GetRequiredService<DbConnection>()); see AddEntityFrameworkCore. " +
                "Entity Framework Core keeps only the first options registration, so a host AddDbContext " +
                "called after AddEntityFrameworkCore is ignored: register the host context first and " +
                "configure it with the test connection.");
        }

        if (session.Transaction is { } transaction)
        {
            await context.Trace
                .Operation("sql.enlist", $"SQL · enlist {typeof(TContext).Name}", TraceSource)
                .During(ProtoTracePhase.Setup)
                .With("db.context", typeof(TContext).FullName)
                .RunAsync(() =>
                {
                    dbContext.Database.UseTransaction(transaction);
                    return ValueTask.CompletedTask;
                });
        }
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}
