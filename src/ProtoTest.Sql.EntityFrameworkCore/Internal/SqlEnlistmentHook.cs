namespace ProtoTest.Sql.EntityFrameworkCore.Internal;

using Microsoft.EntityFrameworkCore;
using ProtoTest.Core;
using ProtoTest.Sql;

/// <summary>
/// Enlists the test's <see cref="DbContext"/> in the transaction ProtoTest.Sql opened. Without this the
/// provider refuses to execute, because ADO.NET requires a command to carry the transaction of a
/// connection that has a pending local transaction.
/// </summary>
internal sealed class SqlEnlistmentHook<TContext> : IProtoTestHook
    where TContext : DbContext
{
    // Runs after the connection hook, which opens the connection and starts the transaction.
    public int Order => -999;

    public Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var session = context.Service<ProtoSqlSession>();
        if (session.Transaction is { } transaction)
        {
            var dbContext = context.Service<TContext>();
            if (!ReferenceEquals(dbContext.Database.GetDbConnection(), session.Connection))
            {
                throw new InvalidOperationException(
                    $"The DbContext '{typeof(TContext).Name}' uses its own connection, so it cannot enlist in " +
                    "the test's transaction. Configure it with the connection ProtoTest owns, for example " +
                    "UseNpgsql(services.GetRequiredService<DbConnection>()); see AddEntityFrameworkCore.");
            }

            dbContext.Database.UseTransaction(transaction);
        }

        return Task.CompletedTask;
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;
}
