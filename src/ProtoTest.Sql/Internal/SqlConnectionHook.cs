namespace ProtoTest.Sql.Internal;

using System.Data.Common;
using ProtoTest.Core;

/// <summary>
/// Opens the test's connection, starts its transaction when the isolation strategy needs one, and owns
/// both as a resource released before the test's clients are disposed.
/// </summary>
internal sealed class SqlConnectionHook(SqlOptions options) : IProtoTestHook
{
    public int Order => -1_000;

    public async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var session = context.Service<ProtoSqlSession>();

        // The resource is registered before the connection is opened: an open or begin failure must
        // still own what it created, so a started transaction is rolled back and the connection is
        // disposed during teardown instead of escaping the test's ownership.
        var sharing = options.SharedWith.Count == 0
            ? string.Empty
            : $" · shared with {string.Join(", ", options.SharedWith)}";
        context.RegisterResource(new ProtoResource(
            "database:connection",
            "database",
            $"{session.Connection.GetType().Name} · {options.Isolation}{sharing}",
            release => ReleaseAsync(session, release)));

        using var open = context.Trace
            .Operation("sql.connection.open", $"SQL · open {session.Connection.GetType().Name}", "ProtoTest.Sql")
            .During(ProtoTracePhase.Setup)
            .With("sql.connection.type", session.Connection.GetType().FullName)
            .Begin();
        try
        {
            await session.Connection.OpenAsync();
            if (options.Isolation == SqlIsolation.Transaction)
            {
                await context.Trace
                    .Operation("sql.transaction.begin", "SQL · begin transaction", "ProtoTest.Sql")
                    .During(ProtoTracePhase.Setup)
                    .With("sql.isolation", options.Isolation.ToString())
                    .RunAsync(async () => session.Transaction = await session.Connection.BeginTransactionAsync());
            }

            open.Succeed();
        }
        catch (Exception exception)
        {
            open.Fail(exception);
            throw;
        }
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    private async ValueTask ReleaseAsync(ProtoSqlSession session, ProtoResourceReleaseContext release)
    {
        var transaction = session.Transaction;
        try
        {
            if (transaction is not null)
            {
                await release.Trace
                    .Operation("sql.transaction.rollback", "SQL · rollback transaction", "ProtoTest.Sql")
                    .During(release.Phase)
                    .With("sql.isolation", options.Isolation.ToString())
                    .RunAsync(async () => await transaction.RollbackAsync(release.CancellationToken));
            }
        }
        finally
        {
            // Teardown must release both even when rollback fails: the transaction first, then the
            // connection, each in its own finally.
            session.Transaction = null;
            try
            {
                if (transaction is not null)
                {
                    await transaction.DisposeAsync();
                }
            }
            finally
            {
                await session.Connection.DisposeAsync();
            }
        }
    }
}
