namespace ProtoTest.Sql.Internal;

using System.Data.Common;
using ProtoTest.Core;

/// <summary>
/// Opens the test's connection, starts its transaction when the isolation strategy needs one, and owns
/// both as a resource released before the test's clients are disposed.
/// </summary>
internal sealed class SqlConnectionHook(ProtoSqlOptions options) : IProtoTestHook
{
    public int Order => -1_000;

    public async Task BeforeTestAsync(ProtoExecutionContext context)
    {
        var session = context.Service<ProtoSqlSession>();
        await session.Connection.OpenAsync();
        if (options.Isolation == SqlIsolation.TransactionPerTest)
        {
            session.Transaction = await session.Connection.BeginTransactionAsync();
        }

        context.RegisterResource(new ProtoResource(
            "database:connection",
            "database",
            $"{session.Connection.GetType().Name} · {options.Isolation}",
            release => ReleaseAsync(session, release)));
    }

    public Task AfterTestAsync(ProtoExecutionContext context) => Task.CompletedTask;

    private static async ValueTask ReleaseAsync(ProtoSqlSession session, ProtoResourceReleaseContext release)
    {
        if (session.Transaction is { } transaction)
        {
            await transaction.RollbackAsync(release.CancellationToken);
            await transaction.DisposeAsync();
            session.Transaction = null;
        }

        await session.Connection.DisposeAsync();
    }
}
